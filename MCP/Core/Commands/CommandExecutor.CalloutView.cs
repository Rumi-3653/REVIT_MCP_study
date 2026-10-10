using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    /// <summary>
    /// 在父視圖上建立詳圖索引（Callout）視圖——大樣圖的起點
    /// </summary>
    public partial class CommandExecutor
    {
        #region 詳圖索引視圖

        private object CreateCalloutView(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument?.Document
                ?? throw new Exception("沒有開啟中的文件");
            const double mmPerFt = 304.8;

            // ── 父視圖 ────────────────────────────────────────────────────
            IdType parentIdVal = parameters["parentViewId"]?.Value<IdType>() ?? 0;
            View parent = parentIdVal != 0
                ? doc.GetElement(parentIdVal.ToElementId()) as View
                : _uiApp.ActiveUIDocument.ActiveView;
            if (parent == null)
                throw new Exception(parentIdVal != 0 ? $"ElementId {parentIdVal} 不是視圖或不存在" : "無法取得作用中視圖");
            if (parent.IsTemplate || !(parent is ViewPlan || parent is ViewSection))
                throw new Exception($"父視圖必須是平面、剖面、立面或詳圖視圖，目前是 {parent.ViewType}（{parent.Name}）");

            // ── 範圍：收集模型點（ft）──────────────────────────────────────
            var pts = new List<XYZ>();
            var elementIds = parameters["elementIds"] as JArray;
            var minTok = parameters["minPoint"] as JObject;
            var maxTok = parameters["maxPoint"] as JObject;
            if (elementIds != null && elementIds.Count > 0)
            {
                double pad = (parameters["paddingMm"]?.Value<double>() ?? 300.0) / mmPerFt;
                var missing = new List<IdType>();
                foreach (var tok in elementIds)
                {
                    IdType idVal = tok.Value<IdType>();
                    BoundingBoxXYZ bb = doc.GetElement(idVal.ToElementId())?.get_BoundingBox(null);
                    if (bb == null) { missing.Add(idVal); continue; }
                    var lo = bb.Min - new XYZ(pad, pad, pad);
                    var hi = bb.Max + new XYZ(pad, pad, pad);
                    foreach (double x in new[] { lo.X, hi.X })
                        foreach (double y in new[] { lo.Y, hi.Y })
                            foreach (double z in new[] { lo.Z, hi.Z })
                                pts.Add(bb.Transform.OfPoint(new XYZ(x, y, z)));
                }
                if (pts.Count == 0)
                    throw new Exception($"elementIds 都沒有 BoundingBox 或不存在：{string.Join(", ", missing)}");
            }
            else if (minTok != null && maxTok != null)
            {
                Func<JObject, XYZ> toXyz = o => new XYZ(
                    (o["x"]?.Value<double>() ?? 0) / mmPerFt,
                    (o["y"]?.Value<double>() ?? 0) / mmPerFt,
                    (o["z"]?.Value<double>() ?? 0) / mmPerFt);
                pts.Add(toXyz(minTok));
                pts.Add(toXyz(maxTok));
            }
            else
            {
                throw new Exception("必須指定範圍：minPoint+maxPoint 或 elementIds 二擇一");
            }

            // 投影到父視圖平面（Right/Up）取外接矩形，再還原成模型點
            XYZ origin = parent.Origin, right = parent.RightDirection, up = parent.UpDirection;
            var us = pts.Select(p => (p - origin).DotProduct(right)).ToList();
            var vs = pts.Select(p => (p - origin).DotProduct(up)).ToList();
            double uMin = us.Min(), uMax = us.Max(), vMin = vs.Min(), vMax = vs.Max();
            if (uMax - uMin < 1e-3 || vMax - vMin < 1e-3)
                throw new Exception("範圍投影到父視圖後寬或高為 0，請確認點位或元素是否與視圖平面平行");
            XYZ p1 = origin + right * uMin + up * vMin;
            XYZ p2 = origin + right * uMax + up * vMax;

            // ── 視圖類型 / 樣版 ─────────────────────────────────────────
            string vftName = parameters["viewFamilyTypeName"]?.Value<string>();
            var vfts = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().ToList();
            ViewFamilyType vft = string.IsNullOrEmpty(vftName)
                ? vfts.FirstOrDefault(t => t.ViewFamily == ViewFamily.Detail)
                : vfts.FirstOrDefault(t => t.Name == vftName);
            if (vft == null)
                throw new Exception(string.IsNullOrEmpty(vftName) ? "專案中找不到 Detail 視圖類型" : $"找不到視圖類型: {vftName}");

            string templateName = parameters["viewTemplateName"]?.Value<string>();
            View template = null;
            if (!string.IsNullOrEmpty(templateName))
            {
                template = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                    .FirstOrDefault(v => v.IsTemplate && v.Name == templateName);
                if (template == null)
                    throw new Exception($"找不到視圖樣版: {templateName}");
            }

            int scale = parameters["scale"]?.Value<int>() ?? 0;
            string viewName = parameters["viewName"]?.Value<string>();
            bool dryRun = parameters["dryRun"]?.Value<bool>() ?? false;

            Func<XYZ, object> mm = p => new { X = Math.Round(p.X * mmPerFt, 1), Y = Math.Round(p.Y * mmPerFt, 1), Z = Math.Round(p.Z * mmPerFt, 1) };
            var range = new
            {
                Point1Mm = mm(p1),
                Point2Mm = mm(p2),
                WidthMm = Math.Round((uMax - uMin) * mmPerFt, 1),
                HeightMm = Math.Round((vMax - vMin) * mmPerFt, 1)
            };

            if (dryRun)
            {
                return new
                {
                    DryRun = true,
                    ParentViewId = parent.Id.GetIdValue(),
                    ParentViewName = parent.Name,
                    ViewFamilyType = vft.Name,
                    Scale = scale > 0 ? (int?)scale : null,
                    ViewTemplate = template?.Name,
                    Range = range
                };
            }

            View callout;
            using (Transaction trans = TransactionHelper.Begin(doc, "建立詳圖索引視圖"))
            {
                trans.Start();
                callout = ViewSection.CreateCallout(doc, parent.Id, vft.Id, p1, p2);
                if (!string.IsNullOrEmpty(viewName))
                    callout.Name = EnsureUniqueViewName(doc, viewName);
                if (scale > 0)
                    callout.Scale = scale;
                if (template != null)
                    callout.ViewTemplateId = template.Id;
                trans.Commit();
            }

            return new
            {
                ViewId = callout.Id.GetIdValue(),
                ViewName = callout.Name,
                ViewType = callout.ViewType.ToString(),
                Scale = callout.Scale,
                ParentViewId = parent.Id.GetIdValue(),
                ParentViewName = parent.Name,
                ViewFamilyType = vft.Name,
                ViewTemplate = template?.Name,
                Range = range
            };
        }

        #endregion
    }
}
