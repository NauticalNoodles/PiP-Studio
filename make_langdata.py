"""Regenerates LangData.cs from the translations. Run it after editing any of them:  python make_langdata.py

strings.tsv        English <tab> Arabic <tab> Chinese (Simplified), one line per piece of text (every piece of text is here)
lang/<code>.tsv    English <tab> translation, for the other languages (es, pt, fr, de, ru, ja, ...)

A line missing from a lang file just stays English; a line whose English text is no longer in strings.tsv is left out."""
import os, glob

here = os.path.dirname(os.path.abspath(__file__))

def read(path, cols):
    rows = [l.rstrip('\n').split('\t') for l in open(path, encoding='utf-8') if l.strip()]
    for i, r in enumerate(rows, 1):
        if len(r) != cols:
            raise SystemExit('%s line %d: expected %d columns separated by tabs, found %d' % (os.path.basename(path), i, cols, len(r)))
    return rows

base = read(os.path.join(here, 'strings.tsv'), 3)
english = [r[0] for r in base]
langs = [('ar', 'Arabic', {r[0]: r[1] for r in base}), ('zh', 'Chinese', {r[0]: r[2] for r in base})]
for path in sorted(glob.glob(os.path.join(here, 'lang', '*.tsv'))):
    code = os.path.splitext(os.path.basename(path))[0]
    tr = {r[0]: r[1] for r in read(path, 2)}
    missing = [e for e in english if e not in tr]
    stale = [k for k in tr if k not in set(english)]
    if missing: print('lang/%s.tsv: %d lines not translated yet (they stay English)' % (code, len(missing)))
    if stale: print('lang/%s.tsv: %d lines no longer used' % (code, len(stale)))
    langs.append((code, code.capitalize(), tr))

def cs(s):
    # "\n" in the file stays a newline escape in C#
    return '"' + s.replace('\\n', '\x00').replace('\\', '\\\\').replace('"', '\\"').replace('\x00', '\\n') + '"'

out = ['using System.Collections.Generic;', '', 'namespace PipStudio', '{',
       '    /// <summary>Generated from strings.tsv and lang/*.tsv by make_langdata.py: English text → each language.</summary>',
       '    static class LangData', '    {',
       '        public static readonly string[] Codes = { %s };' % ', '.join('"%s"' % c for c, _, _ in langs), '',
       '        /// <summary>The translations for a language code, or null for English (or a code there are none for).</summary>',
       '        public static Dictionary<string, string> For(string code)', '        {', '            switch (code)', '            {']
out += ['                case "%s": return %s();' % (c, n) for c, n, _ in langs]
out += ['                default: return null;', '            }', '        }']
for code, name, tr in langs:
    out += ['', '        static Dictionary<string, string> %s()' % name, '        {',
            '            return new Dictionary<string, string>', '            {']
    out += ['                { %s, %s },' % (cs(e), cs(tr[e])) for e in english if e in tr]
    out += ['            };', '        }']
out += ['    }', '}', '']
open(os.path.join(here, 'LangData.cs'), 'w', encoding='utf-8').write('\n'.join(out))
print('LangData.cs: %d strings, %d languages' % (len(english), len(langs) + 1))
