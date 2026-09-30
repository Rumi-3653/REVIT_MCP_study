---
name: concrete-quantity-takeoff
description: "混凝土數量統計 SOP（calculate_concrete_quantity）：結構體五類（柱/梁/樓板/結構牆/基礎）依 Revit 幾何淨體積、按 樓層×品類×類型（含結構材料）分組加總 m³，可另存 Excel。定義納入/排除口徑、樓層歸屬規則、與明細表/採購數量的差異。當使用者提到混凝土數量、混凝土算量、混凝土體積、RC 數量、concrete quantity、concrete takeoff、澆置量估算時觸發。"
metadata:
  version: "1.0"
  updated: "2026-09-30"
  created: "2026-09-30"
  contributors:
    - "Rumi-3653"
  references:
    - "MCP/Core/Commands/CommandExecutor.ConcreteQuantity.cs"
    - "MCP-Server/src/tools/structure-tools.ts"
    - "Revit API BuiltInParameter.HOST_VOLUME_COMPUTED"
  related:
    - scaffold-takeoff.md
    - quantity-takeoff-excel.md
    - tool-capability-boundary.md
  referenced_by: []
  tags: [混凝土, 數量, 算量, concrete, quantity, takeoff, 體積, m³, 結構體, calculate_concrete_quantity]
---

# 混凝土數量統計 SOP (calculate_concrete_quantity)

> **狀態：v1 已實作，尚未在真機 Revit 專案複驗**（實作於 2026-09-30，R24/R25 build 通過）。本工具回的是**模型幾何量**，不是採購量、不是估驗量；對外報數字前必須依本檔〈與其他數字的差異〉逐項交代口徑。

## 目的與邊界

**目的**：不改模型、一次呼叫，得到全案（或指定樓層/視圖）結構體混凝土體積，依「樓層 × 品類 × 類型（含結構材料）」分組，供進度/採購/估驗**交叉核對**用。

**邊界（硬限制）**：

* ✅ 唯讀；`exportExcel=true` 只寫一個 xlsx 到專案資料夾。
* ❌ 不算連結模型（RevitLinkInstance）內的元件——`tool-capability-boundary.md` 列為不可達；分棟/分標的模型請各自開檔各跑一次。
* ❌ 不做損耗、搭接、施工縫、超挖等加成；要加成請在 Excel 彙總表另加一欄係數。
* ❌ 不依材料過濾（見〈為什麼依品類不依材料〉）；材料名只回報供人工確認。
* ❌ 不含樓梯、女兒牆、水箱、機械基礎、雜項 RC——這些品類（OST_Stairs / 一般模型 / 泛用族群）不在五類內，需另計。

## 納入口徑（五類）

| 參數值 | Revit 品類 | 納入條件（`structuralOnly=true` 預設） |
|---|---|---|
| `column` | OST_StructuralColumns 結構柱 | 全部 |
| `beam` | OST_StructuralFraming 結構構架（含大梁/小梁/地梁/桁架構件） | 全部 |
| `floor` | OST_Floors 樓板 | 類型勾「結構」（FLOOR_PARAM_IS_STRUCTURAL=1）；未勾者視為建築面層樓板排除 |
| `structuralWall` | OST_Walls 牆 | 實例勾「結構」（WALL_STRUCTURAL_SIGNIFICANT=1）；未勾者視為隔間/帷幕排除 |
| `foundation` | OST_StructuralFoundation 結構基礎（獨立基腳/連續基腳/筏基板） | 全部 |

`structuralOnly=false` 時樓板與牆全部納入——只在模型沒有正確勾「結構」時當作救急，並在回報中標明。

**建築柱（OST_Columns）刻意不納入**：建築柱多為包柱/裝修柱，非結構混凝土。若專案把 RC 柱誤建成建築柱，先改模型再算，不在工具端放寬。

## 體積來源

* 每個元件讀 `HOST_VOLUME_COMPUTED`（Revit「體積」參數），由 ft³ 換算 m³（× 0.028316846592）。
* 這是 Revit 的**幾何淨量**：已扣除接合（Join）讓渡的部分、開口（門窗洞/穿孔/樓板開口）與 void 切除。梁柱接頭誰扣誰由模型的 Join 順序決定，工具不重算。
* 體積為 0 或無值的元件（未閉合、線型、族群無實體）略過並在 `Warnings` 回報略過數。

## 樓層歸屬規則

依品類慣用參數，全部失敗才退回 `Element.LevelId`：

| 品類 | 優先參數 |
|---|---|
| 柱 | FAMILY_BASE_LEVEL_PARAM（基準樓層） |
| 梁 | INSTANCE_REFERENCE_LEVEL_PARAM（參考樓層） |
| 樓板 | LEVEL_PARAM |
| 牆 | WALL_BASE_CONSTRAINT（底部約束） |
| 基礎 | FAMILY_LEVEL_PARAM → LEVEL_PARAM |

含意：跨層柱整根歸**底部**樓層；梁歸其參考樓層（通常是梁頂所在層）。這與工地「N 層澆置量 = N 層柱 + N+1 層梁板」的口徑**不同**——對照澆置紀錄時要把梁板往下挪一層，或用 `levelNames` 分兩次跑再自己配。找不到樓層的元件歸 `(無樓層)`。

## 分組與輸出

* `Groups`：樓層 × 品類 × 族群 × 類型 × 結構材料，各含 `Count` 與 `Volume_m3`（小數 3 位），依樓層高程排序。
* `ByLevel` / `ByCategory`：小計。`Summary.TotalVolume_m3`：總計。
* `Elements`（`includeElements=true`）：逐元件 ElementId/樓層/類型/體積，供追溯；大模型會很長，預設不回。
* `ExcelPath`（`exportExcel=true`）：兩個工作表——「彙總」（同 Groups + 合計列 SUM 公式）與「明細」（逐元件）。預設存在 .rvt 同資料夾，未存檔的模型存桌面。

結構材料解析順序：實例 STRUCTURAL_MATERIAL_PARAM → 類型 STRUCTURAL_MATERIAL_PARAM → 複合結構「結構」層材料 → 任一材料 → 空字串（`Warnings` 會提示筆數）。

## 為什麼依品類不依材料

多數實務模型的結構材料欄是空的、或全部套預設「混凝土」而沒分強度；若依材料名含「混凝土」過濾，空欄模型會回 0，看起來像沒混凝土。因此口徑定為**品類決定納入、材料只回報**：使用者從 `Groups` 的 Material 欄看到空白或非混凝土（例如鋼骨梁 SS400），自行剔除該列。強度分組（fc' 210/280/350）靠**類型名稱**或材料名稱區分，模型沒分就分不了——工具不猜。

## 與其他數字的差異（對外報數前必讀）

| 對照對象 | 差在哪 |
|---|---|
| Revit 明細表「體積」 | 同源（同一個 Volume 參數），只是本工具已幫你依樓層×品類×類型分組；明細表若有 Calculated Value 或材料取量，本工具讀不到。 |
| 採購/澆置量 | 本工具無損耗、無施工縫、無超挖；澆置量通常比模型量多 2–5%。 |
| 估驗計價數量 | 估驗依合約數量計算規則（例如梁柱接頭歸屬、樓板扣梁與否）可能與 Revit Join 結果不同，只能當交叉核對，不能直接當計價依據。 |
| 結構計算書數量 | 計算書常用軸線淨長×斷面，不扣接合；本工具是幾何淨量，會偏小。 |

## 使用流程

1. 確認活動文件是目標結構模型（連結模型算不到）。
2. 先跑 `calculate_concrete_quantity`（預設全模型、五類、`structuralOnly=true`），看 `Warnings`：略過非結構樓板/牆的數量是否合理、是否大量元件無材料。
3. 需要對照某層澆置量：加 `levelNames`；需要逐元件追溯：加 `includeElements=true`。
4. 要交出去：`exportExcel=true`，回報 `ExcelPath`；在回覆中明列口徑（幾何淨量、無損耗、樓層歸屬規則）。
5. 數字對外前：`Per domain/concrete-quantity-takeoff.md` 交代口徑，並提醒不可直接當估驗/採購數量。

## 已知缺口

* 連結模型元件、樓梯/女兒牆/雜項 RC 不在範圍。
* 澆置分區（施工縫）無法表達；同一樓板跨分區只能整片歸一層。
* 尚無真機專案複驗；第一次在實案跑完，請把 Revit 明細表總體積與 `Summary.TotalVolume_m3` 的差異記回本檔。
