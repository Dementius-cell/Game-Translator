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
