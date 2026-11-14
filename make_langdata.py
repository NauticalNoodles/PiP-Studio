"""Regenerates LangData.cs from strings.tsv (English <tab> Arabic <tab> Chinese, one line per piece of text).
Run it after editing strings.tsv:  python make_langdata.py"""
import os

here = os.path.dirname(os.path.abspath(__file__))
rows = [l.rstrip('\n').split('\t') for l in open(os.path.join(here, 'strings.tsv'), encoding='utf-8') if l.strip()]
for i, r in enumerate(rows, 1):
    if len(r) != 3:
        raise SystemExit('strings.tsv line %d: expected 3 columns separated by tabs, found %d' % (i, len(r)))

def cs(s):
    # "\n" in the file stays a newline escape in C#
    return '"' + s.replace('\\n', '\x00').replace('\\', '\\\\').replace('"', '\\"').replace('\x00', '\\n') + '"'

out = ['using System.Collections.Generic;', '', 'namespace PipStudio', '{',
       '    /// <summary>Generated from strings.tsv by make_langdata.py: English text → Arabic / Chinese (Simplified).</summary>',
       '    static class LangData', '    {']
for name, idx in (('Arabic', 1), ('Chinese', 2)):
    out += ['        public static Dictionary<string, string> %s()' % name, '        {',
            '            return new Dictionary<string, string>', '            {']
    out += ['                { %s, %s },' % (cs(r[0]), cs(r[idx])) for r in rows]
    out += ['            };', '        }']
out += ['    }', '}', '']
open(os.path.join(here, 'LangData.cs'), 'w', encoding='utf-8').write('\n'.join(out))
print('LangData.cs: %d strings' % len(rows))
