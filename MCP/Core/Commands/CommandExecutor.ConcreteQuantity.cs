using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using ClosedXML.Excel;
using Newtonsoft.Json.Linq;

#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    /// <summary>
    /// 混凝土數量統計（calculate_concrete_quantity）。
    /// 口徑見 domain/concrete-quantity-takeoff.md：結構體五類（柱/梁/樓板/結構牆/基礎），
    /// 體積取 Revit HOST_VOLUME_COMPUTED（幾何淨量，已扣接合與開口），依 樓層×品類×類型 分組，
    /// 不做損耗/搭接加成。唯讀；exportExcel=true 時另存 xlsx 到專案資料夾。
    /// </summary>
    public partial class CommandExecutor
    {
        private const double CubicFeetToCubicMeters = 0.028316846592;

        private static readonly Dictionary<string, BuiltInCategory> ConcreteCategoryMap =
            new Dictionary<string, BuiltInCategory>(StringComparer.OrdinalIgnoreCase)
            {
                { "column", BuiltInCategory.OST_StructuralColumns },
                { "beam", BuiltInCategory.OST_StructuralFraming },
                { "floor", BuiltInCategory.OST_Floors },
                { "structuralWall", BuiltInCategory.OST_Walls },
                { "foundation", BuiltInCategory.OST_StructuralFoundation },
            };

        private static readonly Dictionary<string, string> ConcreteCategoryLabel =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "column", "柱" }, { "beam", "梁" }, { "floor", "樓板" },
                { "structuralWall", "結構牆" }, { "foundation", "基礎" },
            };

        private class ConcreteElementRow
        {
            public IdType ElementId;
            public string CategoryKey;
            public string Level;
            public double LevelElevationMm;
            public string FamilyName;
            public string TypeName;
            public string Material;
            public double VolumeM3;
        }

        private object CalculateConcreteQuantity(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            var categoryKeys = parameters?["categories"]?.Select(t => t.Value<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (categoryKeys == null || categoryKeys.Count == 0)
                categoryKeys = ConcreteCategoryMap.Keys.ToList();
            var unknown = categoryKeys.Where(k => !ConcreteCategoryMap.ContainsKey(k)).ToList();
            if (unknown.Count > 0)
                throw new Exception("Unknown categories: " + string.Join(", ", unknown) + ". Allowed: " + string.Join(", ", ConcreteCategoryMap.Keys));

            var levelFilter = new HashSet<string>(
                parameters?["levelNames"]?.Select(t => t.Value<string>()).Where(s => !string.IsNullOrWhiteSpace(s)) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            bool activeViewOnly = parameters?["activeViewOnly"]?.Value<bool>() ?? false;
            bool structuralOnly = parameters?["structuralOnly"]?.Value<bool>() ?? true;
            bool includeElements = parameters?["includeElements"]?.Value<bool>() ?? false;
            bool exportExcel = parameters?["exportExcel"]?.Value<bool>() ?? false;

            var rows = new List<ConcreteElementRow>();
            var warnings = new List<string>();
            int skippedNonStructural = 0, skippedNoVolume = 0, skippedLevelFilter = 0;

            foreach (string key in categoryKeys)
            {
                var collector = activeViewOnly
                    ? new FilteredElementCollector(doc, _uiApp.ActiveUIDocument.ActiveView.Id)
                    : new FilteredElementCollector(doc);
                var elements = collector.OfCategory(ConcreteCategoryMap[key]).WhereElementIsNotElementType().ToElements();

                foreach (Element e in elements)
                {
                    if (structuralOnly && !IsStructuralConcreteCandidate(e, key)) { skippedNonStructural++; continue; }

                    Parameter volParam = e.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
                    double volFt3 = volParam != null && volParam.HasValue ? volParam.AsDouble() : 0.0;
                    if (volFt3 <= 1e-9) { skippedNoVolume++; continue; }

                    Level level = ResolveConcreteElementLevel(doc, e, key);
                    string levelName = level != null ? level.Name : "(無樓層)";
                    if (levelFilter.Count > 0 && !levelFilter.Contains(levelName)) { skippedLevelFilter++; continue; }

                    ElementType type = doc.GetElement(e.GetTypeId()) as ElementType;
                    rows.Add(new ConcreteElementRow
                    {
                        ElementId = e.Id.GetIdValue(),
                        CategoryKey = key,
                        Level = levelName,
                        LevelElevationMm = level != null ? Math.Round(level.Elevation * FeetToMillimeters, 1) : double.MinValue,
                        FamilyName = type != null ? type.FamilyName : "",
                        TypeName = type != null ? type.Name : e.Name,
                        Material = ResolveStructuralMaterialName(doc, e, type),
                        VolumeM3 = volFt3 * CubicFeetToCubicMeters,
                    });
                }
            }

            if (skippedNonStructural > 0) warnings.Add($"略過 {skippedNonStructural} 個非結構元件（樓板未勾「結構」/ 牆未勾「結構」）；structuralOnly=false 可納入。");
            if (skippedNoVolume > 0) warnings.Add($"略過 {skippedNoVolume} 個無體積元件（Volume 參數為 0 或不存在，例如未閉合/純線型元件）。");
            if (skippedLevelFilter > 0) warnings.Add($"levelNames 過濾掉 {skippedLevelFilter} 個元件。");
            int noMaterial = rows.Count(r => string.IsNullOrEmpty(r.Material));
            if (noMaterial > 0) warnings.Add($"{noMaterial} 個元件讀不到結構材料，Material 欄為空；本工具依品類統計、不依材料過濾，請人工確認是否為混凝土。");

            var groups = rows
                .GroupBy(r => new { r.Level, r.LevelElevationMm, r.CategoryKey, r.FamilyName, r.TypeName, r.Material })
                .OrderBy(g => g.Key.LevelElevationMm).ThenBy(g => Array.IndexOf(ConcreteCategoryMap.Keys.ToArray(), g.Key.CategoryKey)).ThenBy(g => g.Key.TypeName)
                .Select(g => new
                {
                    g.Key.Level,
                    Category = g.Key.CategoryKey,
                    CategoryLabel = ConcreteCategoryLabel[g.Key.CategoryKey],
                    g.Key.FamilyName,
                    g.Key.TypeName,
                    g.Key.Material,
                    Count = g.Count(),
                    Volume_m3 = Math.Round(g.Sum(r => r.VolumeM3), 3),
                })
                .ToList();

            var byLevel = rows.GroupBy(r => new { r.Level, r.LevelElevationMm }).OrderBy(g => g.Key.LevelElevationMm)
                .Select(g => new { g.Key.Level, Count = g.Count(), Volume_m3 = Math.Round(g.Sum(r => r.VolumeM3), 3) }).ToList();
            var byCategory = categoryKeys.Select(k => new
            {
                Category = k,
                CategoryLabel = ConcreteCategoryLabel[k],
                Count = rows.Count(r => r.CategoryKey == k),
                Volume_m3 = Math.Round(rows.Where(r => r.CategoryKey == k).Sum(r => r.VolumeM3), 3),
            }).ToList();

            string excelPath = null;
            if (exportExcel)
            {
                try { excelPath = ExportConcreteQuantityExcel(doc, rows, parameters); }
                catch (Exception ex) { warnings.Add("Excel 匯出失敗：" + ex.Message); }
            }

            return new
            {
                Method = "domain/concrete-quantity-takeoff.md",
                Scope = new { Categories = categoryKeys, LevelNames = levelFilter.ToList(), ActiveViewOnly = activeViewOnly, StructuralOnly = structuralOnly },
                Summary = new
                {
                    ElementCount = rows.Count,
                    TotalVolume_m3 = Math.Round(rows.Sum(r => r.VolumeM3), 3),
                    LevelCount = byLevel.Count,
                    GroupCount = groups.Count,
                },
                ByLevel = byLevel,
                ByCategory = byCategory,
                Groups = groups,
                Elements = includeElements
                    ? rows.OrderBy(r => r.LevelElevationMm).ThenBy(r => r.CategoryKey).ThenBy(r => r.ElementId)
                        .Select(r => new { r.ElementId, r.Level, Category = r.CategoryKey, r.FamilyName, r.TypeName, r.Material, Volume_m3 = Math.Round(r.VolumeM3, 4) }).ToList<object>()
                    : null,
                ExcelPath = excelPath,
                Warnings = warnings,
            };
        }

        /// <summary>樓板/牆只有勾「結構」的才算結構體；柱/梁/基礎品類本身即結構。</summary>
        private static bool IsStructuralConcreteCandidate(Element e, string categoryKey)
        {
            if (categoryKey == "floor")
            {
                Parameter p = e.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL);
                return p == null || !p.HasValue || p.AsInteger() == 1;
            }
            if (categoryKey == "structuralWall")
            {
                Parameter p = e.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT);
                return p != null && p.HasValue && p.AsInteger() == 1;
            }
            return true;
        }

        /// <summary>依品類慣用的樓層參數解析，全部失敗才退回 Element.LevelId。</summary>
        private static Level ResolveConcreteElementLevel(Document doc, Element e, string categoryKey)
        {
            BuiltInParameter[] candidates;
            switch (categoryKey)
            {
                case "column": candidates = new[] { BuiltInParameter.FAMILY_BASE_LEVEL_PARAM, BuiltInParameter.SCHEDULE_LEVEL_PARAM }; break;
                case "beam": candidates = new[] { BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM, BuiltInParameter.SCHEDULE_LEVEL_PARAM }; break;
                case "floor": candidates = new[] { BuiltInParameter.LEVEL_PARAM, BuiltInParameter.SCHEDULE_LEVEL_PARAM }; break;
                case "structuralWall": candidates = new[] { BuiltInParameter.WALL_BASE_CONSTRAINT }; break;
                default: candidates = new[] { BuiltInParameter.FAMILY_LEVEL_PARAM, BuiltInParameter.LEVEL_PARAM, BuiltInParameter.SCHEDULE_LEVEL_PARAM }; break;
            }
            foreach (var bip in candidates)
            {
                Parameter p = e.get_Parameter(bip);
                if (p == null || !p.HasValue) continue;
                Level lv = doc.GetElement(p.AsElementId()) as Level;
                if (lv != null) return lv;
            }
            return doc.GetElement(e.LevelId) as Level;
        }

        /// <summary>結構材料：實例 → 類型 STRUCTURAL_MATERIAL_PARAM → 複合結構「結構」層材料 → 任一材料。</summary>
        private static string ResolveStructuralMaterialName(Document doc, Element e, ElementType type)
        {
            foreach (Element holder in new[] { e, (Element)type })
            {
                Parameter p = holder?.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM);
                if (p != null && p.HasValue)
                {
                    var m = doc.GetElement(p.AsElementId()) as Material;
                    if (m != null) return m.Name;
                }
            }
            var host = type as HostObjAttributes;
            var cs = host?.GetCompoundStructure();
            if (cs != null)
            {
                var structural = cs.GetLayers().FirstOrDefault(l => l.Function == MaterialFunctionAssignment.Structure);
                if (structural != null)
                {
                    var m = doc.GetElement(structural.MaterialId) as Material;
                    if (m != null) return m.Name;
                }
            }
            var anyId = e.GetMaterialIds(false).FirstOrDefault();
            return anyId != null ? (doc.GetElement(anyId) as Material)?.Name ?? "" : "";
        }

        private string ExportConcreteQuantityExcel(Document doc, List<ConcreteElementRow> rows, JObject parameters)
        {
            string outputPath = parameters?["outputPath"]?.Value<string>();
            if (string.IsNullOrEmpty(outputPath))
            {
                string projectDir = string.IsNullOrEmpty(doc.PathName)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                    : System.IO.Path.GetDirectoryName(doc.PathName);
                outputPath = System.IO.Path.Combine(projectDir, $"混凝土數量_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
            }

            using (var wb = new XLWorkbook())
            {
                var headerBg = XLColor.FromHtml("#4472C4");

                // Sheet 1：彙總（樓層×品類×類型）
                var ws = wb.Worksheets.Add("彙總");
                string[] headers = { "樓層", "品類", "族群", "類型", "結構材料", "數量", "體積(m³)" };
                for (int c = 0; c < headers.Length; c++)
                {
                    var cell = ws.Cell(1, c + 1);
                    cell.Value = headers[c];
                    cell.Style.Font.SetBold().Font.SetFontColor(XLColor.White);
                    cell.Style.Fill.SetBackgroundColor(headerBg);
                }
                int row = 2;
                var grouped = rows
                    .GroupBy(r => new { r.Level, r.LevelElevationMm, r.CategoryKey, r.FamilyName, r.TypeName, r.Material })
                    .OrderBy(g => g.Key.LevelElevationMm).ThenBy(g => g.Key.CategoryKey).ThenBy(g => g.Key.TypeName);
                foreach (var g in grouped)
                {
                    ws.Cell(row, 1).Value = g.Key.Level;
                    ws.Cell(row, 2).Value = ConcreteCategoryLabel[g.Key.CategoryKey];
                    ws.Cell(row, 3).Value = g.Key.FamilyName;
                    ws.Cell(row, 4).Value = g.Key.TypeName;
                    ws.Cell(row, 5).Value = g.Key.Material;
                    ws.Cell(row, 6).Value = g.Count();
                    ws.Cell(row, 7).Value = Math.Round(g.Sum(r => r.VolumeM3), 3);
                    row++;
                }
                ws.Cell(row, 1).Value = "合計";
                ws.Cell(row, 6).FormulaA1 = $"SUM(F2:F{row - 1})";
                ws.Cell(row, 7).FormulaA1 = $"SUM(G2:G{row - 1})";
                ws.Range(row, 1, row, headers.Length).Style.Font.SetBold();
                ws.Column(7).Style.NumberFormat.Format = "0.000";
                ws.Columns().AdjustToContents();

                // Sheet 2：明細（逐元件，供追溯）
                var wsD = wb.Worksheets.Add("明細");
                string[] dHeaders = { "ElementId", "樓層", "品類", "族群", "類型", "結構材料", "體積(m³)" };
                for (int c = 0; c < dHeaders.Length; c++)
                {
                    var cell = wsD.Cell(1, c + 1);
                    cell.Value = dHeaders[c];
                    cell.Style.Font.SetBold().Font.SetFontColor(XLColor.White);
                    cell.Style.Fill.SetBackgroundColor(headerBg);
                }
                int dRow = 2;
                foreach (var r in rows.OrderBy(r => r.LevelElevationMm).ThenBy(r => r.CategoryKey).ThenBy(r => r.ElementId))
                {
                    wsD.Cell(dRow, 1).Value = (double)r.ElementId;
                    wsD.Cell(dRow, 2).Value = r.Level;
                    wsD.Cell(dRow, 3).Value = ConcreteCategoryLabel[r.CategoryKey];
                    wsD.Cell(dRow, 4).Value = r.FamilyName;
                    wsD.Cell(dRow, 5).Value = r.TypeName;
                    wsD.Cell(dRow, 6).Value = r.Material;
                    wsD.Cell(dRow, 7).Value = Math.Round(r.VolumeM3, 4);
                    dRow++;
                }
                wsD.Column(7).Style.NumberFormat.Format = "0.0000";
                wsD.Columns().AdjustToContents();

                wb.SaveAs(outputPath);
            }
            return outputPath;
        }
    }
}
