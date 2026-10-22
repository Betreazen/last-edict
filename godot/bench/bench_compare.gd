# Сравнивает два JSON-файла, сохранённых bench.gd с флагом --save=.
# Запуск: godot --headless --path . --script res://bench/bench_compare.gd -- bench/before.json bench/after.json [порог-%]
# Вердикт «faster»/«slower» ставится, только если медиана сдвинулась больше порога (по умолчанию 5%)
# и межквартильные диапазоны прогонов не пересекаются. Иначе «same» (в пределах шума).
# Код выхода: 0 — регрессий нет, 1 — есть хотя бы одно «slower», 2 — ошибка ввода.
extends SceneTree


func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() < 2:
		printerr("usage: godot --headless --script bench_compare.gd -- <base.json> <diff.json> [threshold-%]")
		quit(2)
		return
	var threshold := args[2].trim_suffix("%").to_float() / 100.0 if args.size() > 2 else 0.05
	var base := _load(args[0])
	var diff := _load(args[1])
	if base.is_empty() or diff.is_empty():
		quit(2)
		return
	if base["header"] != diff["header"]:
		print("WARNING: different builds:\n  base: %s\n  diff: %s" % [base["header"], diff["header"]])

	var regressions := 0
	print("%-24s %12s %12s %8s  %s" % ["Benchmark", "Base median", "Diff median", "Change", "Verdict"])
	var base_results: Dictionary = base["results"]
	var diff_results: Dictionary = diff["results"]
	for name: String in base_results:
		if not diff_results.has(name):
			print("%-24s %12s %12s %8s  missing in diff" % [name, "", "", ""])
			continue
		var base_samples: Array = base_results[name]
		var diff_samples: Array = diff_results[name]
		var b := _quartiles(base_samples)
		var d := _quartiles(diff_samples)
		var change := d[1] / b[1] - 1.0
		var verdict := "same"
		if change <= -threshold and d[2] < b[0]:
			verdict = "faster"
		elif change >= threshold and d[0] > b[2]:
			verdict = "slower"
			regressions += 1
		print("%-24s %9d us %9d us %+7.1f%%  %s" % [name, b[1], d[1], change * 100.0, verdict])
	for name: String in diff_results:
		if not base_results.has(name):
			print("%-24s %12s %12s %8s  new (no baseline)" % [name, "", "", ""])
	quit(1 if regressions > 0 else 0)


# [Q1, медиана, Q3] по отсортированным замерам.
func _quartiles(samples: Array) -> PackedFloat64Array:
	var s := samples.duplicate()
	s.sort()
	var n := s.size()
	return PackedFloat64Array([s[n / 4], s[n / 2], s[(3 * n) / 4]])


func _load(path: String) -> Dictionary:
	if path.is_relative_path():
		path = ProjectSettings.globalize_path("res://").path_join(path)
	var text := FileAccess.get_file_as_string(path)
	if text.is_empty():
		printerr("cannot read %s: %s" % [path, error_string(FileAccess.get_open_error())])
		return {}
	var parsed: Variant = JSON.parse_string(text)
	var data: Dictionary = parsed if parsed is Dictionary else {}
	if not (data.has("results") and data.has("header")):
		printerr("%s is not a bench.gd report" % path)
		return {}
	return data
