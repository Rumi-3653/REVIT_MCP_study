/**
 * 結構分析工具 — structure Profile
 */

import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const structureTools: Tool[] = [
    {
        name: "analyze_beam_penetration",
        description: "分析特定結構梁上的套管穿孔。回傳精確的幾何數據，如距離柱心長度、梁深度、開孔直徑等。",
        inputSchema: {
            type: "object",
            properties: {
                beamId: { type: "number", description: "要分析的目標結構梁 Element ID" },
                diameterParamNames: { type: "array", items: { type: "string" }, description: "可選。搜尋套管『直徑』的動態參數名稱清單（實體與類型自動 Fallback）。預設為 ['開孔直徑', '直徑', '管徑', 'Diameter', 'Size']" },
                lengthParamNames: { type: "array", items: { type: "string" }, description: "可選。搜尋套管『長度』的動態參數名稱清單。預設為 ['長度', 'Length']" },
                widthParamNames: { type: "array", items: { type: "string" }, description: "可選。搜尋梁『寬度』的動態參數名稱清單。預設為 ['b', '梁寬', 'Width']" },
                sleeveIds: { type: "array", items: { type: "number" }, description: "可選。指定檢核的套管 ID 清單，避免全區掃描" },
                linkInstanceId: { type: "number", description: "可選。連結模型的 ID" }
            },
            required: ["beamId"],
        },
    },
    {
        name: "scan_penetrated_beams_in_view",
        description: "掃描目前視圖中所有被套管（Sleeves）穿過的結構梁。回傳包含梁 ID、連結模型 ID 及穿過該梁的套管數量的清單。",
        inputSchema: {
            type: "object",
            properties: {},
        },
    },
    {
        name: "calculate_concrete_quantity",
        description:
            "混凝土數量統計（唯讀）：掃描結構體五類（柱 column / 梁 beam / 樓板 floor / 結構牆 structuralWall / 基礎 foundation），" +
            "讀 Revit 幾何淨體積（Volume 參數，已扣接合與開口），依 樓層×品類×類型（含結構材料）分組加總 m³，另回傳逐樓層、逐品類小計。" +
            "口徑依 domain/concrete-quantity-takeoff.md：不含損耗/搭接加成、不依材料過濾（材料只回報供人工確認）、" +
            "樓板與牆預設只算勾「結構」者。exportExcel=true 時另存 xlsx（彙總+明細兩表）到專案資料夾。連結模型元件不在範圍。",
        inputSchema: {
            type: "object",
            properties: {
                categories: {
                    type: "array",
                    items: { type: "string", enum: ["column", "beam", "floor", "structuralWall", "foundation"] },
                    description: "要統計的品類，預設五類全算。",
                },
                levelNames: { type: "array", items: { type: "string" }, description: "可選。只統計這些樓層名稱（依元件所屬樓層/基準樓層判定）。" },
                activeViewOnly: { type: "boolean", description: "只統計目前視圖可見元件。預設 false（整個模型）。", default: false },
                structuralOnly: { type: "boolean", description: "樓板只算勾「結構」者、牆只算勾「結構」者。預設 true；設 false 會把建築樓板/非結構牆也納入。", default: true },
                includeElements: { type: "boolean", description: "回傳逐元件明細（ElementId/樓層/類型/體積）。大模型會很長，預設 false。", default: false },
                exportExcel: { type: "boolean", description: "另存 Excel（彙總表+逐元件明細表）。預設 false。", default: false },
                outputPath: { type: "string", description: "可選。Excel 輸出完整路徑；省略時存到專案 .rvt 所在資料夾，檔名 混凝土數量_時間戳.xlsx。" },
            },
        },
    },
];
