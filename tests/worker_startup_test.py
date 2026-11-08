"""Synthetic worker startup protocol tests; no GPU or third-party imports required."""
import contextlib
import importlib.util
import io
import json
import pathlib
import sys
import unittest
from unittest.mock import patch

sys.dont_write_bytecode = True
path = pathlib.Path(__file__).resolve().parents[1] / 'src/GameTranslator.Infrastructure/Ocr/paddle_text_detector_worker.py'
spec = importlib.util.spec_from_file_location('worker', path)
worker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(worker)


class StartupTests(unittest.TestCase):
    def test_failure_categories_do_not_expose_messages(self):
        cases = [(worker.ModelUnavailableError('private'), 'model-missing'),
                 (ModuleNotFoundError('private module'), 'dependency-missing'),
                 (ImportError('private dll path'), 'native-library-load-failed'),
                 (PermissionError('secret file'), 'permission-denied'),
                 (worker.FrameDecodeError('private file'), 'input-read-failed'),
                 (MemoryError('private allocation'), 'memory-exhausted'),
                 (RuntimeError('CUDA out of memory private'), 'gpu-out-of-memory'),
                 (RuntimeError('cuDNN private'), 'gpu-execution-failed'),
                 (RuntimeError('out of memory private'), 'memory-exhausted'),
                 (RuntimeError('private input'), 'inference-failed')]
        for error, code in cases:
            self.assertEqual(code, worker.failure_code(error))

    def test_inference_error_protocol_is_redacted_and_keeps_request_id(self):
        class Detector:
            startup_timing = None
            def detect(self, *args):
                raise RuntimeError('CUDA out of memory C:/private token=secret')
        output = io.StringIO()
        request = json.dumps(dict(inputPath='private', requestId=7, threshold=.3, boxThreshold=.6, unclipRatio=1.2))
        with patch.object(worker, 'DirectPaddleTextDetector', return_value=Detector()), patch.object(sys, 'stdin', io.StringIO(request)), contextlib.redirect_stdout(output):
            worker.run_worker()
        error = json.loads(output.getvalue().splitlines()[-1])
        self.assertEqual('gpu-out-of-memory', error['errorCode'])
        self.assertEqual(7, error['requestId'])
        self.assertNotIn('private', output.getvalue())
        self.assertNotIn('secret', output.getvalue())

    def test_missing_driver_is_classified(self):
        with patch('ctypes.WinDLL', side_effect=OSError('private-path')):
            with self.assertRaises(worker.CudaUnavailableError):
                worker.verify_cuda_driver()

    def test_zero_devices_and_driver_failure_are_classified(self):
        class Driver:
            def cuInit(self, flags):
                return 0
            def cuDeviceGetCount(self, count):
                return 0  # initialized count remains zero
        with patch('ctypes.WinDLL', return_value=Driver()):
            with self.assertRaises(worker.CudaUnavailableError):
                worker.verify_cuda_driver()
        with patch('ctypes.WinDLL') as loader:
            loader.return_value.cuInit.return_value = 100
            with self.assertRaises(worker.CudaUnavailableError):
                worker.verify_cuda_driver()

    def test_failures_emit_only_fixed_codes(self):
        for error, code in [(worker.CudaUnavailableError('private'), 'cuda-unavailable'),
                            (RuntimeError('private-path'), 'initialization-failed')]:
            output = io.StringIO()
            with patch.object(worker, 'DirectPaddleTextDetector', side_effect=error), contextlib.redirect_stdout(output):
                worker.run_worker()
            self.assertEqual({'status': 'startup-error', 'errorCode': code}, json.loads(output.getvalue()))


if __name__ == '__main__':
    unittest.main()
