import argparse
import json
import math
import os
import sys
from pathlib import Path


SCRIPT_DIRECTORY = Path(__file__).resolve().parent
os.environ.setdefault("PADDLE_PDX_CACHE_HOME", str(SCRIPT_DIRECTORY / "paddlex-cache"))
os.environ.setdefault("PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK", "True")
MODEL_DIRECTORY = (
    Path(os.environ["PADDLE_PDX_CACHE_HOME"])
    / "official_models"
    / "PP-OCRv6_medium_det"
)


def to_bounds(points):
    if hasattr(points, "tolist"):
        points = points.tolist()
    if not points:
        return None

    normalized = []
    for point in points:
        if hasattr(point, "tolist"):
            point = point.tolist()
        if not isinstance(point, (list, tuple)) or len(point) < 2:
            continue
        normalized.append((float(point[0]), float(point[1])))
    if not normalized:
        return None

    left = math.floor(min(point[0] for point in normalized))
    top = math.floor(min(point[1] for point in normalized))
    right = math.ceil(max(point[0] for point in normalized))
    bottom = math.ceil(max(point[1] for point in normalized))
    if right <= left or bottom <= top:
        return None
    return {"x": left, "y": top, "width": right - left, "height": bottom - top}


def write_message(payload):
    print(json.dumps(payload, separators=(",", ":")), flush=True)


class DirectPaddleTextDetector:
    """Runs the bundled PP-OCR detector without high-level PaddleOCR startup."""

    def __init__(self):
        import cv2
        import numpy as np
        import paddle
        from paddlex.inference.models.text_detection.processors import (
            DBPostProcess,
            DetResizeForTest,
            NormalizeImage,
        )

        model_file = MODEL_DIRECTORY / "inference.json"
        parameters_file = MODEL_DIRECTORY / "inference.pdiparams"
        if not model_file.is_file() or not parameters_file.is_file():
            raise RuntimeError("Bundled PP-OCRv6_medium_det model is unavailable.")

        config = paddle.inference.Config(str(model_file), str(parameters_file))
        config.disable_glog_info()
        config.enable_use_gpu(1024, 0)
        config.switch_ir_optim(True)

        self.cv2 = cv2
        self.np = np
        self.predictor = paddle.inference.create_predictor(config)
        self.input_handle = self.predictor.get_input_handle(
            self.predictor.get_input_names()[0]
        )
        self.output_handle = self.predictor.get_output_handle(
            self.predictor.get_output_names()[0]
        )
        self.resize = DetResizeForTest(limit_side_len=1216, limit_type="max")
        self.normalize = NormalizeImage(order="hwc")
        self.postprocess = DBPostProcess(
            thresh=0.3,
            box_thresh=0.6,
            unclip_ratio=1.2,
            max_candidates=3000,
            box_type="quad",
        )

    def detect(self, source_path):
        image = self.cv2.imread(str(source_path), self.cv2.IMREAD_COLOR)
        if image is None:
            raise RuntimeError("PaddleOCR could not decode the candidate frame.")

        resized_images, image_shapes = self.resize([image], 1216, "max")
        normalized_images = self.normalize(resized_images)
        tensor = self.np.expand_dims(
            self.np.transpose(normalized_images[0], (2, 0, 1)), axis=0
        ).astype(self.np.float32)
        self.input_handle.copy_from_cpu(tensor)
        self.predictor.run()
        output = self.output_handle.copy_to_cpu()
        polygons, scores = self.postprocess([output], image_shapes, 0.3, 0.6, 1.2)

        candidates = []
        for polygon, score in zip(polygons[0], scores[0]):
            bounds = to_bounds(polygon)
            if bounds is None:
                continue
            bounds["confidence"] = float(score)
            candidates.append(bounds)
        return candidates


def run_worker():
    detector = DirectPaddleTextDetector()
    write_message({"status": "ready"})
    for line in sys.stdin:
        try:
            request = json.loads(line)
            input_path = Path(request["inputPath"])
            candidates = detector.detect(input_path)
            write_message({"status": "ok", "candidates": candidates})
        except Exception:
            write_message({"status": "error", "error": "PaddleOCR detection failed.", "candidates": []})


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--worker", action="store_true")
    arguments = parser.parse_args()
    if not arguments.worker:
        raise SystemExit("The PaddleOCR candidate detector must run in worker mode.")
    run_worker()


if __name__ == "__main__":
    main()
