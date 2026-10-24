extends SceneTree
# Сжатие HD-слоя (Р19, tools/hd/hd_layer.py import --compress): каждый PNG в assets/ мода — рядом DDS в BC7 (BPTC) с mip-уровнями.
# Загрузчики (GfxTextures, ImageScales.Texture) берут DDS, если он есть и не старше PNG: в видеопамяти ≈ ×0,33 от RGBA с mip-уровнями.
#   godot --headless --path godot --script res://bench/hd_compress.gd -- <каталог мода> [--part k/n]
# --part k/n — только каждый n-й файл начиная с k-го (несколько процессов сжимают слой вместе, tools/hd/hd_layer.py --jobs).
# DDS пишется во временный файл и переименовывается: оборванная запись (Ctrl+C, нет места) не выглядит готовым сжатием.

func _init() -> void:
	var args := OS.get_cmdline_user_args()
	if args.is_empty():
		printerr("hd_compress: нужен каталог мода")
		quit(2)
		return
	var root: String = args[0].path_join("assets")
	var files := []
	_collect(root, files)
	files.sort()
	var at := args.find("--part")
	if at >= 0:
		var kn: PackedStringArray = args[at + 1].split("/") if at + 1 < args.size() else PackedStringArray()
		if kn.size() != 2 or not kn[0].is_valid_int() or not kn[1].is_valid_int() or int(kn[1]) < 1:
			printerr("hd_compress: --part k/n — например 0/4")
			quit(2)
			return
		var k := int(kn[0])
		var n := int(kn[1])
		var mine := []
		for i in files.size():
			if i % n == k:
				mine.append(files[i])
		files = mine
	var done := 0
	var failed := 0
	var skipped := 0
	var raw := 0
	var packed := 0
	var t0 := Time.get_ticks_msec()
	for png in files:
		var dds: String = png.get_basename() + ".dds"
		if FileAccess.file_exists(dds) and FileAccess.get_modified_time(dds) >= FileAccess.get_modified_time(png):
			skipped += 1
			continue
		var image := Image.load_from_file(png)
		if image == null:
			printerr("hd_compress: не читается ", png)
			failed += 1
			continue
		image.convert(Image.FORMAT_RGBA8)
		raw += image.get_data_size()
		image.generate_mipmaps()
		var tmp := dds + ".part"
		if image.compress(Image.COMPRESS_BPTC) != OK or image.save_dds(tmp) != OK or DirAccess.rename_absolute(tmp, dds) != OK:
			printerr("hd_compress: не сжат ", png)
			DirAccess.remove_absolute(tmp)
			failed += 1
			continue
		packed += image.get_data_size()
		done += 1
	print("hd_compress: сжато %d, уже было %d, ошибок %d, %.1f с; RGBA %.1f МБ → BC7 с mip-уровнями %.1f МБ" % [done, skipped, failed, (Time.get_ticks_msec() - t0) / 1000.0, raw / 1048576.0, packed / 1048576.0])
	quit(1 if failed > 0 else 0)


func _collect(dir: String, out: Array) -> void:
	var d := DirAccess.open(dir)
	if d == null:
		return
	for name in d.get_files():
		if name.to_lower().ends_with(".png"):
			out.append(dir.path_join(name))
	for sub in d.get_directories():
		_collect(dir.path_join(sub), out)
