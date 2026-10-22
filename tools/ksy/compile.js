const fs = require('fs'), yaml = require('js-yaml');
const KSC = require('kaitai-struct-compiler');
const ksy = yaml.load(fs.readFileSync('sg.ksy', 'utf8'));
const compiler = typeof KSC === 'function' ? new KSC() : (KSC.default ? new KSC.default() : KSC);
const run = (compiler.compile || KSC.compile).bind(compiler);
run('csharp', ksy, null, false).then(files => {
  fs.mkdirSync('out', { recursive: true });
  for (const [name, code] of Object.entries(files)) fs.writeFileSync('out/' + name, code);
  console.log('files:', Object.keys(files).join(', '));

  const outDir = '../../src/Import.Legacy.Formats/Sg';
  fs.mkdirSync(outDir, { recursive: true });
  // Кодировка строк — один экземпляр (Cp1251.cs): поиск по имени на каждое поле идёт под замком поставщика кодовых страниц.
  let generated = '#nullable disable\n' + files['Sg.cs'].split('System.Text.Encoding.GetEncoding("windows-1251")').join('Cp1251.Encoding');
  // Чтения без выделений (SgRead.cs): целые — BinaryReader вместо byte[] на каждое число, имя поля записи — ожидаемая строка,
  // маска тумана — побайтно. Каждая подстановка обязана сработать: иначе генератор поменял код, и правило надо пересмотреть.
  const swap = (from, to, what) => {
    const parts = generated.split(from);
    if (parts.length < 2) throw new Error('подстановка не сработала: ' + what);
    generated = parts.join(to);
  };
  swap('m_io.ReadU4le()', 'm_io.ReadUInt32()', 'u4le');
  swap('m_io.ReadU2le()', 'm_io.ReadUInt16()', 'u2le');
  swap('Cp1251.Encoding.GetString(m_io.ReadBytes(NameValue.Length))', 'SgRead.Name(m_io, NameValue)', 'имя записи');
  const fogLoop = /_fogMask = new List<bool>\(\);\s*for \(var i = 0; i < Fog\.Value \* 8; i\+\+\)\s*\{\s*_fogMask\.Add\(m_io\.ReadBitsIntBe\(1\) != 0\);\s*\}/;
  if (!fogLoop.test(generated)) throw new Error('подстановка не сработала: маска тумана');
  generated = generated.replace(fogLoop, '_fogMask = SgRead.BitsBe(m_io, Fog.Value * 8);');
  // Списки с известным числом элементов — сразу нужной ёмкости (SgRead.Capacity): рост по одному с копиями был заметен в профиле
  // харнессов (разбор сохранений, perf-log «Э2 — разбор .sg»). Порядок и состав элементов те же.
  const listLoop = /(_\w+) = new List<([^>]+)>\(\);(\s*)for \(var i = 0; i < ([^;]+); i\+\+\)/g;
  const lists = (generated.match(listLoop) || []).length;
  if (lists === 0) throw new Error('подстановка не сработала: ёмкость списков');
  generated = generated.replace(listLoop, '$1 = new List<$2>(SgRead.Capacity($4));$3for (var i = 0; i < $4; i++)');
  console.log('списков с ёмкостью: ' + lists);
  fs.writeFileSync(outDir + '/Sg.g.cs', generated);
  console.log('copied: out/Sg.cs -> ' + outDir + '/Sg.g.cs (+ #nullable disable, Cp1251.Encoding, SgRead)');
}).catch(e => { console.error('ERR', e.message || e); process.exit(1); });
