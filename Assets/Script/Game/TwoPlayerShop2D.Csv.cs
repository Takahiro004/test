using System.Collections.Generic;
using System.Text;
using UnityEngine;

// 商品のステータスを CSV(表)から読み込む(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「商品の一覧」→ Product Csv)
// 再生を始めたときに1回だけ読む。表の1行目(見出し)で、どの列が何かを決める(列の順番は自由。知らない列は使わない):
//   ID(あれば。ID の無い行は読まない) / 商品名(必須) / 値段 / ランク / 献立の食材(★・○・1 なら献立の食材) / 重さ / 割れる(★・○・1 なら割れる) / 割れる重さ / 大きさ / モデルの大きさ
//   - 見出しに「g」が付いた重さ(例: 重さ（g）)は、csvGramsPerWeight グラムを重さ 1 として直す
//   - 値段があってランクの列が無ければ、ランク(仮の図形の色)は値段から決める
//   - 空のマスは読まない(Inspector の商品の一覧の値のまま)
//   - 重さが1つでも入っていたら、仮の重さ(Use Temp Weights)をやめて表の重さを使う
// 商品名で、Inspector の商品の一覧と合わせる。画像・3Dモデルは表に書けないので、Inspector の一覧のまま。
//   - 表にあって一覧に無い商品は、一覧の最後に足す(画像なし。再生中だけ)
//   - 一覧にあって表に無い商品は、そのまま残す(消さない)
// Excel で保存するときは「CSV UTF-8(コンマ区切り)」を選ぶ(ふつうの「CSV」だと日本語が読めない)。
// 再生中に一覧に入った値は、再生をやめると元に戻る(シーンは書き換えない)。
public partial class TwoPlayerShop2D
{
    void ApplyProductCsv()
    {
        if (productCsv == null) return;

        string text;
        try { text = new UTF8Encoding(false, true).GetString(productCsv.bytes); }
        catch (System.Exception)
        {
            Debug.LogError($"[Shop2D] {productCsv.name} を読めませんでした。Excel で「CSV UTF-8(コンマ区切り)」を選んで保存し直してください");
            return;
        }
        if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);   // 先頭の BOM

        var rows = ParseCsv(text);
        if (rows.Count < 2) { Debug.LogWarning($"[Shop2D] {productCsv.name} に商品の行がありません"); return; }

        // 見出しから、どの列が何か決める
        var header = rows[0];
        int cId = -1, cName = -1, cPrice = -1, cRank = -1, cMenu = -1, cWeight = -1, cFragile = -1, cBreak = -1, cSize = -1, cModelSize = -1;
        bool weightInGrams = false;
        for (int c = 0; c < header.Count; c++)
        {
            string h = header[c].Trim();
            if (h.ToUpperInvariant() == "ID") cId = c;
            else if (h.Contains("商品名") || h == "名前") cName = c;
            else if (h.Contains("値段") || h.Contains("金額") || h.Contains("価格")) cPrice = c;
            else if (h.Contains("ランク")) cRank = c;
            else if (h.Contains("献立") && !h.Contains("使う")) cMenu = c;
            else if (h.Contains("割れる重さ")) cBreak = c;
            else if (h.Contains("割れる")) cFragile = c;
            else if (h.Contains("重さ")) { cWeight = c; weightInGrams = h.Contains("g") || h.Contains("ｇ") || h.Contains("グラム"); }
            else if (h.Contains("モデルの大きさ") || h.Contains("Model Size")) cModelSize = c;
            else if (h.Contains("大きさ") || h == "Size") cSize = c;
        }
        if (cName < 0) { Debug.LogError($"[Shop2D] {productCsv.name} の1行目に「商品名」の列がありません"); return; }

        var list = new List<ProductInfo>(productList ?? new ProductInfo[0]);
        var byName = new Dictionary<string, ProductInfo>();
        foreach (var p in list) if (p != null && !string.IsNullOrEmpty(p.name) && !byName.ContainsKey(p.name)) byName[p.name] = p;

        int updated = 0, weights = 0;
        var added = new List<string>();
        var csvNames = new HashSet<string>();
        for (int r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            string name = Cell(row, cName);
            if (string.IsNullOrEmpty(name) || name == "合計") continue;   // 空の行・合計の行
            if (cId >= 0 && Cell(row, cId) == "") continue;                // ID の列があるときは、ID の無い行(説明の行など)は読まない
            if (cPrice >= 0 && !string.IsNullOrEmpty(Cell(row, cPrice)) && !TryNumber(Cell(row, cPrice), out _)) continue;   // 値段が数字でない行
            if (cPrice >= 0 && Cell(row, cPrice) == "" && !byName.ContainsKey(name)) continue;   // 値段の無い、一覧に無い行は足さない(説明の行など)
            csvNames.Add(name);

            if (!byName.TryGetValue(name, out var info))
            {
                info = new ProductInfo { name = name, shape = (ProductShape)(list.Count % 8) };
                list.Add(info);
                byName[name] = info;
                added.Add(name);
            }

            if (TryNumber(Cell(row, cPrice), out float price)) info.price = Mathf.RoundToInt(price);
            if (TryNumber(Cell(row, cRank), out float rank)) info.rank = Mathf.Clamp(Mathf.RoundToInt(rank), 1, 5);
            else if (cRank < 0 && cPrice >= 0) info.rank = info.price <= 150 ? 1 : info.price <= 300 ? 2 : info.price <= 1000 ? 3 : info.price <= 5000 ? 4 : 5;
            if (cMenu >= 0) info.menuIngredient = IsMark(Cell(row, cMenu));
            if (TryNumber(Cell(row, cWeight), out float w))
            {
                info.weight = weightInGrams ? w / Mathf.Max(0.01f, csvGramsPerWeight) : w;
                weights++;
            }
            if (cFragile >= 0 && Cell(row, cFragile) != "") info.fragile = IsMark(Cell(row, cFragile));
            if (TryNumber(Cell(row, cBreak), out float bl)) info.breakLoad = bl;
            if (TryNumber(Cell(row, cSize), out float size)) info.size = size;
            if (TryNumber(Cell(row, cModelSize), out float ms)) info.modelSize = ms;
            updated++;
        }

        productList = list.ToArray();
        if (weights > 0) useTempWeights = false;   // 表の重さを使う

        var notInCsv = new List<string>();
        foreach (var p in list) if (p != null && !csvNames.Contains(p.name)) notInCsv.Add(p.name);
        Debug.Log($"[Shop2D] {productCsv.name} から {updated} 品を読み込んだ" +
                  (weights > 0 ? $"(重さ {weights} 品。仮の重さはやめた)" : "") +
                  (added.Count > 0 ? $" / 一覧に無いので足した: {string.Join("・", added)}" : "") +
                  (notInCsv.Count > 0 ? $" / 表に無いので一覧のまま: {string.Join("・", notInCsv)}" : ""));
    }

    static string Cell(List<string> row, int c) => c >= 0 && c < row.Count ? row[c].Trim() : "";

    // 数字として読む(「円」「,」「g」などは取りのぞく。空なら false)
    static bool TryNumber(string s, out float value)
    {
        value = 0f;
        if (string.IsNullOrEmpty(s)) return false;
        var sb = new StringBuilder();
        foreach (char ch in s)
        {
            char c = ch >= '０' && ch <= '９' ? (char)('0' + (ch - '０')) : ch == '．' ? '.' : ch;   // 全角の数字も読む
            if (char.IsDigit(c) || c == '.' || c == '-') sb.Append(c);
        }
        return float.TryParse(sb.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    // ★・○・●・1・true・はい なら true
    static bool IsMark(string s)
    {
        s = s.Trim().ToLowerInvariant();
        return s == "★" || s == "☆" || s == "○" || s == "〇" || s == "●" || s == "◎" || s == "1" || s == "true" || s == "yes" || s == "はい";
    }

    // CSV を行とマスに分ける("" で囲んだマスの中のコンマ・改行もそのまま読む)
    static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else quoted = false;
                }
                else cell.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == ',') { row.Add(cell.ToString()); cell.Clear(); }
            else if (ch == '\n' || ch == '\r')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString()); cell.Clear();
                rows.Add(row); row = new List<string>();
            }
            else cell.Append(ch);
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
        return rows;
    }
}
