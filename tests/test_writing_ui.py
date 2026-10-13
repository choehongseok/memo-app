"""Source navigation preservation; native behavior belongs to WindowsChecks."""
import pathlib, subprocess, unittest, xml.etree.ElementTree as E
ROOT=pathlib.Path(__file__).resolve().parents[1]
NS={'w':'http://schemas.microsoft.com/winfx/2006/xaml/presentation'}
X='{http://schemas.microsoft.com/winfx/2006/xaml}Name'
BASE='8a3400dabd1b306fce926c56ab51b32272d16e6f'
def tree(name): return E.parse(ROOT/'src/MemoApp.Windows'/name).getroot()
class WritingUi(unittest.TestCase):
 def test_every_original_command_and_named_control_remains(self):
  for name in ('MainWindow.xaml','StickyNoteWindow.xaml'):
   old=E.fromstring(subprocess.check_output(['git','show',f'{BASE}:src/MemoApp.Windows/{name}'],cwd=ROOT))
   new=tree(name)
   self.assertTrue({e.get('Click') for e in old.iter() if e.get('Click')} <= {e.get('Click') for e in new.iter() if e.get('Click')})
   self.assertTrue({e.get(X) for e in old.iter() if e.get(X)} <= {e.get(X) for e in new.iter() if e.get(X)})
 def test_scaled_workspace_retains_scroll_access(self):
  root=tree('MainWindow.xaml');names={e.get(X):e for e in root.iter() if e.get(X)}
  self.assertIn('WorkspaceScroll',names)
  self.assertEqual(names['WorkspaceScroll'].get('HorizontalScrollBarVisibility'),'Auto')
 def test_footer_is_allocated_before_optional_tools(self):
  root=tree('StickyNoteWindow.xaml');dock=root.find('w:DockPanel',NS)
  children=[e for e in dock if e.tag.endswith('StackPanel')]
  self.assertEqual(children[0].get('DockPanel.Dock'),'Bottom')
 def test_format_buttons_accept_keyboard_focus(self):
  source=(ROOT/'src/MemoApp.Windows/StructuredNoteEditor.cs').read_text()
  line=next(l for l in source.splitlines() if 'private void Button(string label' in l)
  self.assertNotIn('Focusable=false',line)
 def test_sticky_tools_are_optional_and_status_is_visible(self):
  root=tree('StickyNoteWindow.xaml');names={e.get(X):e for e in root.iter() if e.get(X)}
  self.assertIn('SaveState',names)
  self.assertEqual(names['AttachmentTools'].get('IsExpanded'),'False')
  self.assertEqual(names['MoreTools'].get('IsExpanded'),'False')
 def test_unlock_has_separate_creation_and_repair(self):
  names={e.get(X):e for e in tree('MainWindow.xaml').iter() if e.get(X)}
  for name in ('CreateTools','RecoveryTools','BackupSettings'):
   self.assertIn(name,names)
   self.assertEqual(names[name].get('IsExpanded'),'False')
 def test_icon_controls_have_tooltips_and_accessible_names(self):
  icons=[e for e in tree('StickyNoteWindow.xaml').iter() if e.get('Tag')=='Icon']
  self.assertGreaterEqual(len(icons),3)
  for e in icons:
   self.assertTrue(e.get('ToolTip'));self.assertTrue(e.get('AutomationProperties.Name'))
 def test_trash_help_describes_actual_backup_before_purge(self):
  s=(ROOT/'src/MemoApp.Windows/MainWindow.xaml').read_text()
  self.assertTrue('자동 비우기·영구삭제는 제공하지 않습니다' not in s)
  self.assertIn('백업 후',s)
if __name__=='__main__': unittest.main()
