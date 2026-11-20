#!/usr/bin/env python3
"""Static contract validation; actual BAML/layout/behavior is checked on Windows separately."""
import re
from pathlib import Path
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
xaml = root / 'src/DynamicIsland/Shell/SettingsWindow.xaml'
source = xaml.with_suffix('.xaml.cs').read_text()
tree = ET.parse(xaml).getroot()
x = '{http://schemas.microsoft.com/winfx/2006/xaml}'
names = []
def collect(element, scope):
    if element.tag.endswith('ControlTemplate'):
        scope = []  # Each ControlTemplate owns its own WPF namescope.
    if x + 'Name' in element.attrib:
        name = element.attrib[x + 'Name']
        assert name not in scope, 'Duplicate x:Name in namescope: ' + name
        scope.append(name)
    for child in element:
        collect(child, scope)
collect(tree, names)
keys = {e.attrib[x + 'Key'] for e in tree.iter() if x + 'Key' in e.attrib}
references = re.findall(r'\{StaticResource ([A-Za-z]\w*)\}', xaml.read_text())
assert set(references) <= keys, 'Missing static resources'
events = {'Click', 'Checked', 'SelectionChanged', 'ValueChanged', 'TextChanged', 'PasswordChanged', 'KeyDown', 'PreviewKeyDown', 'MouseLeftButtonDown'}
handlers = {v for e in tree.iter() for k, v in e.attrib.items() if k in events}
for handler in handlers:
    assert re.search(r'\bvoid\s+' + handler + r'\s*\(', source), 'Missing handler: ' + handler
for target in re.findall(r'\{Binding ElementName=(\w+)\}', xaml.read_text()):
    assert target in names, 'Missing binding target: ' + target
required = '''Navigation AppearancePage TranslationPage NotificationsPage AppsPage ToolsPage BodyScroll
ScaleSlider ScaleReset SkinDefault SkinGlass MonitorOptions DockOptions LiveToggle DirAuto DirZhEn DirEnZh
ApiDefault ApiCustom ApiLibre ApiAzure ApiDeepL ApiRegion ProviderHint ApiUrl ApiModel ApiKey ApiSave ApiRevert NoticeToggle StealthToggle HideName HideAdd
HideBrowse HideCurrent HideRefresh HideList RunningApps ActTranslate ActRecall ActCamera ActLiveTranslate
ActStopwatch ActFocus ActLog UnsavedPrompt SaveAndClose DiscardAndClose ContinueEditing'''.split()
assert set(required) <= set(names), 'Lost setting or action'
assert 'PersistApi' not in source and 'Log.' not in source, 'No per-keystroke persistence or secret logging'
assert 'Appearance' == next(e for e in tree.iter() if e.tag.endswith('ListBoxItem')).attrib['Tag']
print(f'Settings static contract PASS: {len(names)} unique names, {len(handlers)} event handlers, {len(references)} resource references, {len(required)} required controls; binding targets resolved.')
