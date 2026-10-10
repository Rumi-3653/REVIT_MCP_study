import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const viewCreationTools: Tool[] = [
    {
        name: "create_floor_plans_from_template",
        description: "以指定的 FloorPlan 視圖作為範本，在多個 Level 上批次建立新的樓層平面視圖。複製範本的 ViewFamilyType、View Template、Phase、Phase Filter。適用於補齊缺漏的正式建築圖系列（例：A- 前綴的樓層平面圖）。已存在同名 view 時會跳過並在回傳的 Skipped 清單中記錄原因。",
        inputSchema: {
            type: "object",
            properties: {
                templateViewId: {
                    type: "number",
                    description: "要複製設定的範本 FloorPlan 視圖 ElementId（例：A-十層平面圖）"
                },
                creations: {
                    type: "array",
                    description: "要建立的視圖清單；每一項指定 Level 名稱與新視圖名稱",
                    items: {
                        type: "object",
                        properties: {
                            levelName: {
                                type: "string",
                                description: "目標樓層名稱，需與專案 Level 名稱完全一致（例：11FL）"
                            },
                            newName: {
                                type: "string",
                                description: "新視圖名稱；若已存在同名 view 則會跳過"
                            }
                        },
                        required: ["levelName", "newName"]
                    }
                },
                applyViewTemplate: {
                    type: "boolean",
                    description: "是否套用範本的 View Template。預設 true。",
                    default: true
                }
            },
            required: ["templateViewId", "creations"]
        }
    },
    {
        name: "batch_apply_view_template",
        description: "批次將指定的 ViewTemplate 套用到多個 views。支援透過圖紙（sheets）、view ID、view 名稱或名稱子字串來選取目標 views。可搭配 viewTypeFilter 篩選特定視圖類型。dryRun 模式可預覽變更內容而不實際修改。觸發條件：使用者提到批次修改 view template、套用視圖樣版、sheet 中的 view 改 template。",
        inputSchema: {
            type: "object",
            properties: {
                viewTemplateId: {
                    type: "number",
                    description: "目標 ViewTemplate 的 ElementId（與 viewTemplateName 二擇一）"
                },
                viewTemplateName: {
                    type: "string",
                    description: "目標 ViewTemplate 的名稱（與 viewTemplateId 二擇一；精確匹配）"
                },
                sheetIds: {
                    type: "array",
                    items: { type: "number" },
                    description: "圖紙 ElementId 清單 — 套用到這些圖紙上所有 viewport 對應的 views"
                },
                sheetNumbers: {
                    type: "array",
                    items: { type: "string" },
                    description: "圖紙編號清單（如 ['A101', 'A102']）— 套用到這些圖紙上所有 viewport 對應的 views"
                },
                viewIds: {
                    type: "array",
                    items: { type: "number" },
                    description: "目標 view ElementId 清單（精確指定）"
                },
                viewNames: {
                    type: "array",
                    items: { type: "string" },
                    description: "目標 view 名稱清單（精確匹配）"
                },
                viewNameContains: {
                    type: "string",
                    description: "view 名稱包含此字串（substring match，case-insensitive）"
                },
                viewTypeFilter: {
                    type: "array",
                    items: { type: "string" },
                    description: "限定 view type，例: ['FloorPlan', 'CeilingPlan', 'Section', 'Elevation', 'ThreeD']"
                },
                dryRun: {
                    type: "boolean",
                    description: "設為 true 時僅預覽變更清單，不實際修改。預設 false"
                },
                skipIfSameTemplate: {
                    type: "boolean",
                    description: "若 view 已使用相同 template 則跳過。預設 true"
                }
            },
            required: []
        }
    },
    {
        name: "create_callout_view",
        description: "在父視圖（平面/剖面/立面/詳圖）上建立詳圖索引（Callout）視圖，作為大樣圖的起點。範圍二選一：minPoint/maxPoint（模型座標 mm，會投影到父視圖平面取矩形）或 elementIds（取元素 BoundingBox 外擴 paddingMm）。可指定詳圖類型、比例、名稱、視圖樣版。寫入在 Transaction 內、可 Undo；dryRun=true 只回傳將建立的範圍。觸發條件：使用者提到詳圖索引、callout、大樣、局部放大、出大樣圖。",
        inputSchema: {
            type: "object",
            properties: {
                parentViewId: {
                    type: "number",
                    description: "父視圖 ElementId；省略則用作用中視圖。必須是平面、剖面、立面或詳圖視圖"
                },
                minPoint: {
                    type: "object",
                    description: "範圍角點一（模型座標 mm）。與 maxPoint 成對，和 elementIds 二擇一。平面視圖只看 x/y",
                    properties: { x: { type: "number" }, y: { type: "number" }, z: { type: "number" } },
                    required: ["x", "y"]
                },
                maxPoint: {
                    type: "object",
                    description: "範圍角點二（模型座標 mm）",
                    properties: { x: { type: "number" }, y: { type: "number" }, z: { type: "number" } },
                    required: ["x", "y"]
                },
                elementIds: {
                    type: "array",
                    items: { type: "number" },
                    description: "以這些元素的 BoundingBox 聯集決定範圍（與 minPoint/maxPoint 二擇一）"
                },
                paddingMm: {
                    type: "number",
                    description: "elementIds 模式的外擴距離（mm），預設 300",
                    default: 300
                },
                viewFamilyTypeName: {
                    type: "string",
                    description: "詳圖索引的視圖類型名稱（精確匹配）；省略則用第一個 Detail 類型"
                },
                scale: {
                    type: "number",
                    description: "比例分母，例如 20 代表 1:20；省略則沿用 Revit 預設"
                },
                viewName: {
                    type: "string",
                    description: "新視圖名稱；重名時自動加序號"
                },
                viewTemplateName: {
                    type: "string",
                    description: "要套用的視圖樣版名稱（精確匹配，選填）"
                },
                dryRun: {
                    type: "boolean",
                    description: "true 時只回傳將建立的範圍，不寫入模型。預設 false"
                }
            },
            required: []
        }
    }
];
