import { Tool } from "@modelcontextprotocol/sdk/types.js";

/**
 * 綠建材（Green Material）工具：共享參數載入、材質建立、ElementType 複製與構造層指派、
 * 綠建材 Schema 參數寫入、表面樣式，以及門窗／獨立元件 RFA 綠建材導入。
 *
 * 對應 C# 端 handler:
 *   MCP/Core/Commands/CommandExecutor.GM_GreenMaterial.cs
 *   MCP/Core/Commands/CommandExecutor.GM_RfaFamilyInjection.cs
 *   MCP/Core/Commands/CommandExecutor.Material.cs（CreateCustomMaterial）
 * 對應 domain SOP:
 *   domain/GM_catalog.md, domain/GM_keyword-search.md,
 *   domain/GM_parameter-schema.md, domain/GM_rfa-family-injection.md
 */
export const greenMaterialTools: Tool[] = [
    {
        name: "load_shared_parameters",
        description: "載入共享參數檔 (Shared Parameter File) 並將參數綁定至指定品類的 Type 或 Instance 層級。用於在寫入自訂參數前，先確保專案已載入參數定義。例如載入 GreenMaterial_SharedParams.txt 的 64 個綠建材參數（Mat1~Mat6 六槽位）至 Walls/Floors/Ceilings，之後用 set_green_material_type_parameters 寫值。",
        inputSchema: {
            type: "object",
            properties: {
                filePath: {
                    type: "string",
                    description: "共享參數檔的絕對路徑 (如 C:/path/to/GreenMaterial_SharedParams.txt)",
                },
                categories: {
                    type: "array",
                    items: { type: "string" },
                    description: "要綁定的品類名稱清單: Walls, Floors, Ceilings, Windows, Doors, Materials, Roofs, CurtainPanels, Columns（同時綁 OST_Columns 與 OST_StructuralColumns）, StructuralFraming（樑）",
                },
                bindToInstance: {
                    type: "boolean",
                    description: "true = 綁定至 Instance 參數, false = 綁定至 Type 參數 (預設 false)",
                    default: false,
                },
            },
            required: ["filePath", "categories"],
        },
    },
    {
        name: "create_material",
        description: "在 Revit 專案資料庫 (OST_Materials) 中建立獨立 Material 並關聯獨立 AppearanceAssetElement。",
        inputSchema: {
            type: "object",
            properties: {
                materialName: {
                    type: "string",
                    description: "材質名稱，例如 'test材質'",
                },
            },
            required: ["materialName"],
        },
    },
    {
        name: "get_all_materials",
        description: "查詢 Project Materials（材質瀏覽器）清單中所有現存材質，用於在建立材質後主動驗收確認是否已實體存在。",
        inputSchema: {
            type: "object",
            properties: {
                searchKeyword: {
                    type: "string",
                    description: "依名稱關鍵字篩選（如 'GBM'）。留空或 '*' 表示回傳全部材質。",
                },
            },
        },
    },
    {
        name: "duplicate_element_type",
        description: "複製指定 ElementType 建立新類型（如綠建材 Set 專屬牆類型），並依指定的飾面/結構材質名稱實體建立 2 個獨立 Material，套入 Finish1/Structure/Finish2 三層 CompoundStructure 構造層。用於將牆板＋塗料組合 Set 導入為單一牆體 Element Type。",
        inputSchema: {
            type: "object",
            properties: {
                sourceTypeId: { type: "number", description: "來源類型 Element ID（優先選用含粉刷層的型號）" },
                newTypeName: { type: "string", description: "新類型名稱，例如 'TABC_test牆'（禁用中括號）" },
                finishMaterialName: {
                    type: "string",
                    description: "飾面塗料材質名稱，格式須為 'GBM編號_材料名稱'，例如 'GBM0104106_水性漆(居室外用)'",
                },
                structureMaterialName: {
                    type: "string",
                    description: "結構板材材質名稱，格式須為 'GBM編號_材料名稱'，例如 'GBM0103810_無機質NICHIAS NA LUX矽酸鈣板(0.8FK)'",
                },
                finishThicknessMm: { type: "number", description: "飾面層厚度 (mm)", default: 20 },
                structureThicknessMm: { type: "number", description: "結構層厚度 (mm)", default: 150 },
            },
            required: ["sourceTypeId", "newTypeName", "finishMaterialName", "structureMaterialName"],
        },
    },
    {
        name: "duplicate_type_only",
        description: "單純複製指定 ElementType（Wall/Floor/Ceiling 皆可），不修改 CompoundStructure、不建立或指派任何 Material，新類型與來源類型構造層完全一致。用於 TASK-005.5 情境 5「單選非模型綠建材」路徑 A：先複製出一個不影響既有元件的新 Type，再另外呼叫 set_green_material_type_parameters 寫入 adhesive/sealant/waterproofing 等 Construction 欄位。與 duplicate_element_type（寫死板材+塗料兩種材料）、create_single_material_type/create_multi_layer_type（會重新指派構造層材質）不同。",
        inputSchema: {
            type: "object",
            properties: {
                sourceTypeId: { type: "number", description: "來源類型 Element ID（任意 Wall/Floor/Ceiling Type）" },
                newTypeName: { type: "string", description: "新類型名稱" },
            },
            required: ["sourceTypeId", "newTypeName"],
        },
    },
    {
        name: "set_green_material_type_parameters",
        description: "將綠建材共享參數 Schema（GreenMaterial_SharedParams.txt，Mat1~Mat6 六槽位 + Construction 群組共 67 個欄位）實體寫入指定 ElementType 的 Identity Data。參數須已透過 load_shared_parameters 綁定至該 Type 所屬品類（如 Walls），否則對應欄位會列在回傳的 MissingParameters 中。Mat1=主體/牆板，Mat2=面材/塗料，Mat3=附屬/膠材（僅有基本欄位，無 TVOC/Formaldehyde/CNS），Mat4/Mat5/Mat6=追加構造層（欄位與 Mat1/Mat2 同樣完整）。一個 Set 有幾種材料就只傳幾個 matN 物件，其餘留空，不必寫滿 6 組；哪個材料進哪個槽位請依 GM_generate_revit_injection_plan.py 產出的 plan['materialSlotAssignment'] 決定，不要自行猜測順序——非幾何輔助材料（接著劑/填縫劑/防水材料）一樣要填一個 matN 物件（Mat 槽位記錄的是「這個元件用了哪些綠建材」的完整清單，不是只有物理構造層才算數），另外再用 adhesive/sealant/waterproofing 額外補記它們的施工用途，兩者並存不衝突、不是二選一。",
        inputSchema: {
            type: "object",
            properties: {
                typeId: { type: "number", description: "目標 ElementType Element ID（如 duplicate_element_type 建立的新型別）" },
                certified: { type: "boolean", description: "GreenMaterial_Certified：全牆綠建材評定合格狀態" },
                recycledRatio: { type: "number", description: "GreenMaterial_RecycledRatio：再生材料回收摻配率 (%)" },
                acousticNRC: { type: "number", description: "GreenMaterial_AcousticNRC：吸音係數 (NRC / SAA)" },
                mat1: {
                    type: "object",
                    description: "材料1（主體/牆板）",
                    properties: {
                        name: { type: "string" },
                        certNo: { type: "string", description: "綠建材標章證書字號，如 'GBM0103810'" },
                        category: { type: "string" },
                        subCategory: { type: "string" },
                        applicant: { type: "string" },
                        validUntil: { type: "string" },
                        tvoc: { type: "number", description: "TVOC 逸散率 (mg/m2.h)" },
                        formaldehyde: { type: "number", description: "甲醛逸散率 (mg/m2.h)" },
                        cnsSpec: { type: "string" },
                        testItems: { type: "string" },
                        qualifiedItems: { type: "string" },
                    },
                },
                mat2: {
                    type: "object",
                    description: "材料2（面材/塗料）",
                    properties: {
                        name: { type: "string" },
                        certNo: { type: "string" },
                        category: { type: "string" },
                        subCategory: { type: "string" },
                        applicant: { type: "string" },
                        validUntil: { type: "string" },
                        tvoc: { type: "number" },
                        formaldehyde: { type: "number" },
                        cnsSpec: { type: "string" },
                        testItems: { type: "string" },
                        qualifiedItems: { type: "string" },
                    },
                },
                mat3: {
                    type: "object",
                    description: "材料3（附屬/膠材，選填，僅基本欄位）",
                    properties: {
                        name: { type: "string" },
                        certNo: { type: "string" },
                        category: { type: "string" },
                        subCategory: { type: "string" },
                        applicant: { type: "string" },
                        validUntil: { type: "string" },
                    },
                },
                mat4: {
                    type: "object",
                    description: "材料4（追加構造層，欄位與 Mat1/Mat2 同樣完整）",
                    properties: {
                        name: { type: "string" },
                        certNo: { type: "string" },
                        category: { type: "string" },
                        subCategory: { type: "string" },
                        applicant: { type: "string" },
                        validUntil: { type: "string" },
                        tvoc: { type: "number" },
                        formaldehyde: { type: "number" },
                        cnsSpec: { type: "string" },
                        testItems: { type: "string" },
                        qualifiedItems: { type: "string" },
                    },
                },
                mat5: {
                    type: "object",
                    description: "材料5（追加構造層，欄位與 Mat1/Mat2 同樣完整）",
                    properties: {
                        name: { type: "string" },
                        certNo: { type: "string" },
                        category: { type: "string" },
                        subCategory: { type: "string" },
                        applicant: { type: "string" },
                        validUntil: { type: "string" },
                        tvoc: { type: "number" },
                        formaldehyde: { type: "number" },
                        cnsSpec: { type: "string" },
                        testItems: { type: "string" },
                        qualifiedItems: { type: "string" },
                    },
                },
                mat6: {
                    type: "object",
                    description: "材料6（追加構造層，欄位與 Mat1/Mat2 同樣完整）",
                    properties: {
                        name: { type: "string" },
                        certNo: { type: "string" },
                        category: { type: "string" },
                        subCategory: { type: "string" },
                        applicant: { type: "string" },
                        validUntil: { type: "string" },
                        tvoc: { type: "number" },
                        formaldehyde: { type: "number" },
                        cnsSpec: { type: "string" },
                        testItems: { type: "string" },
                        qualifiedItems: { type: "string" },
                    },
                },
                adhesive: { type: "string", description: "GreenMaterial_Adhesive：附著黏貼之接著劑標章資訊，格式 '產品名稱 (標章編號)'（Construction 群組，非幾何輔助材料專用，該材料仍須另外填入對應的 matN）" },
                sealant: { type: "string", description: "GreenMaterial_Sealant：附著填縫之矽利康/密封膠資訊，格式同上" },
                waterproofing: { type: "string", description: "GreenMaterial_Waterproofing：附著塗佈之防水膜資訊，格式同上" },
            },
            required: ["typeId"],
        },
    },
    {
        name: "create_single_material_type",
        description: "情境 2「各別建立」：複製指定 ElementType（Wall/Floor/Ceiling Type）建立新類型，並實體建立一個純淨綠建材 Material，指派到新 Type 的全部 CompoundStructure 層。Type 名稱與 Material 名稱使用同一組字串（GBM編號_材料名稱），不套 TABC_ 前綴。用於一個 Set 裡每個材料各自獨立建 Type 的情境（例如地板材料各別建立），跟 duplicate_element_type（牆板+塗料兩材料合併一個 Type 的單一組合情境）是不同情境，不要混用。",
        inputSchema: {
            type: "object",
            properties: {
                sourceTypeId: { type: "number", description: "來源類型 Element ID（需與目標品類相同，如同為 FloorType）" },
                materialName: {
                    type: "string",
                    description: "同時作為新 Type 名稱與 Material 名稱，格式須為 'GBM編號_材料名稱'，例如 'GBM0104038_托斯卡尼 TOSCANA複合木質地板'",
                },
            },
            required: ["sourceTypeId", "materialName"],
        },
    },
    {
        name: "create_multi_layer_type",
        description: "通用多材料構造層工具：複製指定 ElementType（Wall/Floor/Ceiling 皆可），依任意數量的材料清單建立獨立綠建材 Material，依序套入 CompoundStructure 各層。跟 duplicate_element_type（寫死 2 個材料的牆體 Finish1/Structure/Finish2 三明治）不同，這裡層數、材料、層位機能完全由呼叫端指定，適用於 2 個以上材料、或非 Wall 品類的單一組合情境（例如地板：飾面地磚 Finish1 + 隔音緩衝墊 Substrate + 混凝土 Structure 三層）。layers 陣列請依實際構造由上到下（或由外到內）的順序排列。",
        inputSchema: {
            type: "object",
            properties: {
                sourceTypeId: { type: "number", description: "來源類型 Element ID（需與目標品類相同，如同為 FloorType）" },
                newTypeName: { type: "string", description: "新類型名稱，例如 'TABC_塑膠地板set'" },
                layers: {
                    type: "array",
                    description: "依構造順序排列的層清單",
                    items: {
                        type: "object",
                        properties: {
                            materialName: { type: "string", description: "格式須為 'GBM編號_材料名稱'" },
                            layerFunction: {
                                type: "string",
                                enum: ["Structure", "Substrate", "Insulation", "Finish1", "Finish2", "Membrane"],
                                description: "對應 Revit MaterialFunctionAssignment：Structure=結構核心層，Substrate=底材/緩衝層，Finish1/Finish2=飾面層，Insulation=隔熱層，Membrane=防水膜",
                            },
                            thicknessMm: { type: "number", description: "該層厚度 (mm)，預設 20", default: 20 },
                        },
                        required: ["materialName", "layerFunction"],
                    },
                },
            },
            required: ["sourceTypeId", "newTypeName", "layers"],
        },
    },
    {
        name: "set_material_surface_pattern",
        description: "為綠建材 Material 建立（或重用既有，依名稱去重不重複建立）Model 目標的 Surface Pattern 並套入該材質的表面／剖切樣式。用於地磚 600×600 網格縫線、木地板木紋等依產品規格需要在平面/剖面顯示紋理的飾面材料（TASK-005.2 情境 2）。",
        inputSchema: {
            type: "object",
            properties: {
                materialId: { type: "number", description: "目標材質 Element ID（與 materialName 至少提供一個，優先使用 materialId）" },
                materialName: { type: "string", description: "目標材質名稱，格式須為 'GBM編號_材料名稱'" },
                patternType: {
                    type: "string",
                    enum: ["Grid", "Wood", "None"],
                    description: "Grid=網格縫線（如地磚），Wood=木紋單向紋理線（如木地板），None=清除既有樣式",
                },
                spacingMm: {
                    type: "number",
                    description: "Grid 為縫線間距 (mm)，預設 600（600×600 網格）；Wood 為紋理線間距 (mm)，預設 100",
                },
                target: {
                    type: "string",
                    enum: ["Surface", "Cut", "Both"],
                    description: "套用範圍：Surface=表面樣式（預設，平面圖可見）、Cut=剖切樣式、Both=兩者皆套用",
                    default: "Surface",
                },
            },
            required: ["patternType"],
        },
    },
    {
        name: "inject_green_material_into_family",
        description: "門窗／獨立元件 RFA 綠建材導入（TASK-005.7 / domain/GM_rfa-family-injection.md）：以使用者指定的既有相似 FamilySymbol 為基底，開啟該家族文件 → 立即另存可復原備份（規則2，先於任何修改）→ 在家族文件內新增一個 Type，絕不改動來源 Type（規則1）→ 寫入 Identity Data 與 GreenMaterial_Mat1_* 共享參數、嘗試寫入 GreenMaterial_Certified 全域欄位（best-effort，部分家族會被 Revit 拒絕新增此 YESNO 欄位，屬已知限制，失敗不影響 Mat1 資料）+ 遮陽係數/隔音等級門窗專屬欄位（規則3）→ 另存為新家族檔名 → LoadFamily 載回專案，且在同一個 Transaction 內做載入前後同名家族 Type 參數簽章快照比對，一偵測到非目標 Type 被異動就整批回滾並報錯，不會靜默覆蓋（規則4）。單一原子呼叫涵蓋整個家族文件生命週期（開啟→備份→編輯→另存→關閉→載回），因為家族文件物件無法跨多次 MCP 呼叫保持開啟。呼叫前必須已由使用者明確指定 sourceTypeId——規則1禁止 AI 自行臆測或無型錄依據挑選基底 Family。",
        inputSchema: {
            type: "object",
            properties: {
                sourceTypeId: { type: "number", description: "使用者指定的基底 FamilySymbol（門或窗，或幕牆嵌板等載入式族群）Element ID，必須是既有的、經使用者確認過的相似型號，不可由 AI 自行挑選" },
                newTypeName: { type: "string", description: "家族文件內新建 Type 的名稱" },
                backupFolder: { type: "string", description: "備份根目錄絕對路徑（也是新家族檔案的存放目錄）。預設專案檔所在目錄下的 _rfa_backup/（若專案尚未儲存過則退回系統暫存目錄）" },
                newFamilySuffix: { type: "string", description: "新家族檔名後綴，預設 '_TABC'，會再接上 mat1.certNo 組成完整後綴以避免撞名", default: "_TABC" },
                sharedParamFilePath: { type: "string", description: "GreenMaterial_SharedParams.txt 的絕對路徑（位於 tools/green-material/）" },
                identityData: {
                    type: "object",
                    description: "Family Type 內建 Identity Data。依 Revit 版本與族群樣板不保證每個欄位都存在，缺的欄位會列在回傳的 MissingParameters",
                    properties: {
                        manufacturer: { type: "string" },
                        model: { type: "string" },
                        description: { type: "string" },
                        url: { type: "string" },
                    },
                },
                mat1: {
                    type: "object",
                    description: "門/窗主材料（玻璃或門扇）綠建材資料，寫入 GreenMaterial_Mat1_*，格式與 set_green_material_type_parameters 的 mat1 相同",
                    properties: {
                        name: { type: "string" },
                        certNo: { type: "string", description: "綠建材標章證書字號，如 'GBM0103810'" },
                        category: { type: "string" },
                        subCategory: { type: "string" },
                        applicant: { type: "string" },
                        validUntil: { type: "string" },
                        tvoc: { type: "number", description: "TVOC 逸散率 (mg/m2.h)，只在有實際數據時才填，不得估算" },
                        formaldehyde: { type: "number", description: "甲醛逸散率 (mg/m2.h)，只在有實際數據時才填，不得估算" },
                        cnsSpec: { type: "string" },
                        testItems: { type: "string" },
                        qualifiedItems: { type: "string" },
                    },
                    required: ["name", "certNo"],
                },
                certified: { type: "boolean", description: "GreenMaterial_Certified：這個 Type 整體的綠建材評定合格狀態（YESNO 全域欄位），語意與 set_green_material_type_parameters 的 certified 相同。通常傳 true。best-effort：部分家族會被 Revit 拒絕新增此欄位並回傳 'Shared parameter creation failed.'，此時會列在回應的 MissingParameters，Mat1 等其餘欄位不受影響。" },
                shadingCoefficient: { type: "number", description: "GreenMaterial_Window_ShadingCoefficient：遮陽係數 Sc。僅 Window/Curtain Wall 案例填，Door 案例應留空（不適用）" },
                acousticRw: { type: "number", description: "GreenMaterial_AcousticRw：隔音等級 Rw (dB)。Window 與 Door 皆適用，只在型錄/測試報告有明確數據時才填" },
            },
            required: ["sourceTypeId", "newTypeName", "sharedParamFilePath", "mat1"],
        },
    },
];
