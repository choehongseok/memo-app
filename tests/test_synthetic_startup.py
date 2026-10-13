"""Static privacy wiring guard; native registry/WPF behavior is a separate gate."""
import pathlib
import re
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]


class SyntheticStartupWiring(unittest.TestCase):
    def test_app_passes_resolved_identity_and_test_registration_guard(self):
        app = (ROOT / "src/MemoApp.Windows/App.xaml.cs").read_text(encoding="utf-8")
        self.assertIn("StartupPaths.ResolveIdentityRoot(options,", app)
        self.assertRegex(app, r"LocalUiIdentity\.GetOrCreate\(identityRoot\)")
        self.assertIn("disableSystemRegistration:options.SyntheticTest", app)
        self.assertNotRegex(app, r"GetOrCreate\([^;]*SpecialFolder\.LocalApplicationData")

    def test_test_registration_returns_before_native_store_construction(self):
        startup = (ROOT / "src/MemoApp.Windows/MainWindow.Startup.cs").read_text(encoding="utf-8")
        guard = re.search(r"if\(disableSystemRegistration\)\s*\{(.*?)\n        \}", startup, re.S)
        self.assertIsNotNone(guard)
        branch = guard.group(1)
        self.assertIn("startupRegistration=null", branch)
        self.assertIn("StartupMenu.IsEnabled=false", branch)
        self.assertIn("return;", branch)
        self.assertNotIn("WindowsStartupStore", branch)
        self.assertLess(guard.end(), startup.index("new WindowsStartupStore()"))


if __name__ == "__main__":
    unittest.main()
