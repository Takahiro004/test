using System.Collections.Generic;
using UnityEngine;

// 献立ミッション(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「献立ミッション」と「機能のON/OFF」→ Menu Mission)
// 1試合に献立は1つだけ。試合の始めに menuList からランダムに1つ選び(2人共通)、1P/2P それぞれの袋の画面に、献立名と食材を出す。
// 袋に入っている食材は ●(緑)、まだのものは ○。食材(menuIngredientCount 品。初めは3品)が全部自分の袋に入ったら「○○完成！」。
// それ以降に袋に入れた商品の金額が menuBonusRate 倍(1.2倍)になる。
//  - 食材は商品の一覧の「名前」で見る(献立の食材の名前と、一覧の名前を同じにする)
//  - 完成は1人1回だけ。完成したあとに食材がこぼれても、倍率はそのまま
//  - 表示(menuPanelLayout が ON。2026-10-30 プログラマーの画面イメージ): 袋の画面の下にプレイヤーの色の帯(menuBandHeight)を敷き、
//    食材のアイコン(商品の一覧の画像。3Dモデルの商品はモデルを撮影した画像 ModelIconSprite。まだの食材は灰色のシルエット、袋に入ったら色付き。
//    画像もモデルも無い商品は名前の札)と、
//    献立のメモ用紙(献立名と食材の一覧。入ったものは ■)を並べる。1P はメモが左・アイコンが右、2P はその逆(menuMemoLeftForP1)
//  - menuPanelLayout が OFF なら前の文字だけの表示(場所と大きさは Menu Label Position / Menu Label Size)
//  - 倍になった商品は、こぼれたときも倍になった金額を引く(袋に入ったときの金額を覚えておく)
[System.Serializable]
public class MenuRecipe
{
    [Tooltip("献立の名前(完成したときに出る)")]
    public string name;
    [Tooltip("食材(商品の一覧の名前と同じにする)")]
    public string[] ingredients;

    public MenuRecipe() { }
    public MenuRecipe(string name, params string[] ingredients) { this.name = name; this.ingredients = ingredients; }

    // 2026-10-23 チームで決めた献立(献立表)
    public static MenuRecipe[] Defaults() => new[]
    {
        new MenuRecipe("カレー", "ぎゅうにく", "じゃがいも", "にんじん", "ぶろっこりー", "たまご"),
        new MenuRecipe("カルボナーラ", "すぱげてぃ", "べーこん", "たまご", "ぎゅうにゅう", "ぶろっこりー"),
        new MenuRecipe("海鮮丼", "まぐろ", "たい", "ひらめ", "ずわいかに", "ゆでえび"),
        new MenuRecipe("肉じゃが", "ぎゅうにく", "じゃがいも", "にんじん", "しょうゆ", "めんつゆ"),
        new MenuRecipe("えびパスタ", "すぱげてぃ", "ゆでえび", "ぶろっこりー", "おりーぶおいる", "ばたー"),
    };
}

public partial class TwoPlayerShop2D
{
    // 袋に入った商品の金額(献立が完成していたら倍にする)
    int BagPrice(Player p, int price)
    {
        if (!menuMission || p.completedMenu == null) return price;
        return Mathf.RoundToInt(price * Mathf.Max(0f, menuBonusRate));
    }

    MenuRecipe currentMenu;                          // この試合の献立(2人共通)
    readonly TextMesh[] menuLabels = new TextMesh[2];

    // 袋の画面の下の献立の表示(帯・食材のアイコン・メモ用紙)
    class MenuPanel
    {
        public Transform root, band, paper;
        public SpriteRenderer[] icons;
        public TextMesh[] iconNames;   // 画像が無い食材の名前の札
        public Material[] silhouettes; // アイコンごとのシルエットのマテリアル(画像を覚えさせるので、1つずつ別にする)
        public TextMesh title;         // 献立名(1行目。長ければ用紙の幅に収まるまで小さくする)
        public TextMesh memo;          // 食材の一覧(2行目から)
        public MenuRecipe builtFor;    // どの献立で作ったか
    }
    readonly MenuPanel[] menuPanels = new MenuPanel[2];
    Material spriteMaterial, silhouetteMaterial;   // ふつうの絵 / 形だけ1色で塗る(まだ袋に入っていない食材)

    // この試合の献立を選ぶ(Start で1回。Menu Fixed Index が 0 以上ならそれ、-1 ならランダム)
    void ChooseMenu()
    {
        currentMenu = null;
        if (menuList == null || menuList.Length == 0) return;
        int i = menuFixedIndex >= 0 && menuFixedIndex < menuList.Length ? menuFixedIndex : Random.Range(0, menuList.Length);
        currentMenu = menuList[i];
        // 必要な食材の数(menuIngredientCount)だけにする。上から順に使う(Menu List そのものは変えない)
        if (currentMenu != null && currentMenu.ingredients != null && menuIngredientCount > 0 && currentMenu.ingredients.Length > menuIngredientCount)
            currentMenu = new MenuRecipe(currentMenu.name, System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Take(currentMenu.ingredients, menuIngredientCount)));
        Debug.Log($"[Shop2D] この試合の献立: {currentMenu?.name}");
    }

    // 袋に入っている商品の名前
    HashSet<string> NamesInBag(Player p)
    {
        var names = new HashSet<string>();
        foreach (var go in p.inBag)
            if (go != null && infoOf.TryGetValue(go, out var info) && !string.IsNullOrEmpty(info.name)) names.Add(info.name);
        return names;
    }

    // この試合の献立の食材が、袋に全部そろったか調べる(商品が袋に入ったときに呼ぶ)
    void CheckMenuMission(Player p)
    {
        var menu = currentMenu;
        if (!menuMission || p.completedMenu != null || menu == null || menu.ingredients == null || menu.ingredients.Length == 0) return;
        var names = NamesInBag(p);
        foreach (var ing in menu.ingredients) if (!names.Contains(ing)) return;

        p.completedMenu = menu.name;
        lastEvent = $"{p.name}が {menu.name} を完成！ これから {menuBonusRate:0.##}倍";
        Debug.Log($"[Shop2D] {lastEvent}");
        StartCoroutine(MenuCompletePopup(p, menu.name));
    }

    // 袋の画面の献立の表示を更新する(Update から毎フレーム。エントリー画面のときも出す)
    void UpdateMenuLabels()
    {
        for (int i = 0; i < 2; i++)
        {
            var p = i == 0 ? p1 : p2;
            if (p == null) continue;
            // 袋持ち上げターンが始まったら隠す(カメラを引くと、画面の下の帯が宙に浮いて見えるため)
            bool show = menuMission && currentMenu != null && currentMenu.ingredients != null && !liftStarted;
            UpdateMenuPanel(i, p, show && menuPanelLayout);
            if (menuPanelLayout)
            {
                if (menuLabels[i] != null) menuLabels[i].gameObject.SetActive(false);
                continue;
            }
            if (menuLabels[i] == null)
            {
                if (!show) continue;
                menuLabels[i] = MakeText($"Menu_{p.name}", null, p.layer, "", Color.white, 1500);
                menuLabels[i].anchor = TextAnchor.UpperLeft;
                menuLabels[i].alignment = TextAlignment.Left;
                menuLabels[i].richText = true;
            }
            var label = menuLabels[i];
            label.gameObject.SetActive(show);
            if (!show) continue;

            Vector2 view = PlayerViewSize();
            label.transform.position = new Vector3(view.x * menuLabelPosition.x, view.y * menuLabelPosition.y + CameraFollowY, 0f);   // 持ち上げでカメラが上がったら一緒に
            SetTextSize(label, HudTextSize * 0.7f * Mathf.Max(0.1f, menuLabelSize));

            var sb = new System.Text.StringBuilder();
            sb.Append($"献立：{currentMenu.name}");
            if (p.completedMenu != null) sb.Append($"  <color=#FFD93D>完成！ {menuBonusRate:0.##}倍</color>");
            var names = NamesInBag(p);
            foreach (var ing in currentMenu.ingredients)
            {
                bool got = p.completedMenu != null || names.Contains(ing);
                sb.Append(got ? $"\n<color=#66FF66>● {ing}</color>" : $"\n○ {ing}");
            }
            label.text = sb.ToString();
        }
    }

    // 袋の画面の下の献立の表示を作り直す・並べる(UpdateMenuLabels から毎フレーム)
    void UpdateMenuPanel(int i, Player p, bool show)
    {
        var panel = menuPanels[i];
        if (!show) { if (panel != null) panel.root.gameObject.SetActive(false); return; }
        if (panel == null || panel.builtFor != currentMenu)
        {
            if (panel != null) Destroy(panel.root.gameObject);
            panel = menuPanels[i] = BuildMenuPanel(p);
        }
        panel.root.gameObject.SetActive(true);

        Vector2 view = PlayerViewSize();
        float bandH = view.y * Mathf.Clamp(menuBandHeight, 0.02f, 0.5f);
        float bottom = -view.y / 2f + CameraFollowY;   // 画面の下の端(持ち上げでカメラが上がったら一緒に)
        Color bandColor = new Color(p.color.r, p.color.g, p.color.b, 1f);

        // 帯
        panel.band.localPosition = new Vector3(0f, bottom + bandH / 2f, 0f);
        panel.band.localScale = new Vector3(view.x, bandH, 1f);
        panel.band.GetComponent<SpriteRenderer>().color = bandColor;

        // メモ用紙(帯より少し上へはみ出す・少し傾ける)。1P は左、2P は右(menuMemoLeftForP1 で入れ替え)
        bool memoLeft = (p == p1) == menuMemoLeftForP1;
        float memoAreaW = view.x * 0.38f;
        float paperW = memoAreaW * 0.85f, paperH = bandH * 1.35f;
        float memoX = memoLeft ? -view.x / 2f + memoAreaW / 2f : view.x / 2f - memoAreaW / 2f;
        var paperSr = panel.paper.GetComponent<SpriteRenderer>();
        paperSr.sprite = menuMemoSprite != null ? menuMemoSprite : GetSquareSprite();
        Vector2 paperSize = paperSr.sprite.bounds.size;
        panel.paper.localPosition = new Vector3(memoX, bottom + bandH * 0.1f + paperH / 2f, 0f);
        panel.paper.localRotation = Quaternion.Euler(0f, 0f, memoLeft ? 4f : -4f);
        panel.paper.localScale = new Vector3(paperW / Mathf.Max(0.01f, paperSize.x), paperH / Mathf.Max(0.01f, paperSize.y), 1f);

        // メモの文字(上に献立名、その下に食材。入ったものは ■)。用紙と一緒に傾ける。
        // 献立名が用紙の幅からはみ出すときは、文字は小さくせずに2行(以上)に折り返す。そのぶん全部の行が少しだけ詰まる。
        // (折り返しても収まらないときだけ、最後の手段として小さくする FitTextWidth)
        var names = NamesInBag(p);
        float textW = paperW * 0.84f;   // 文字を書ける幅(用紙の左右に 8% ずつ余白)
        int ingLines = currentMenu.ingredients.Length;
        int extra = p.completedMenu != null ? 1 : 0;   // 「完成！」の行
        int titleLines = 1;
        float lineSize = 0f;
        List<string> titleParts = null;
        for (int pass = 0; pass < 2; pass++)   // 行の数で文字の大きさが変わり、大きさで折り返しが変わるので、2回合わせる
        {
            lineSize = paperH / (titleLines + extra + ingLines + 1.5f) / 6.4f;   // 1行の高さ ≒ 大きさ x 6.4
            SetTextSize(panel.title, lineSize);
            titleParts = WrapText(panel.title, currentMenu.name, textW);
            titleLines = titleParts.Count;
        }
        lineSize = paperH / (titleLines + extra + ingLines + 1.5f) / 6.4f;
        string title = string.Join("\n", titleParts);
        if (extra > 0) title += $"\n<color=#D08A00>完成！{menuBonusRate:0.##}倍</color>";
        panel.title.text = title;
        var sb = new System.Text.StringBuilder();
        foreach (var ing in currentMenu.ingredients)
        {
            bool got = p.completedMenu != null || names.Contains(ing);
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(got ? $"<color=#1E9E3A>■ {ing}</color>" : $"□ {ing}");
        }
        panel.memo.text = sb.ToString();
        FitTextWidth(panel.title, lineSize, textW);
        FitTextWidth(panel.memo, lineSize, textW);
        Vector3 textTop = panel.paper.localPosition
            + panel.paper.localRotation * new Vector3(-paperW / 2f + paperW * 0.08f, paperH / 2f - paperH * 0.06f, 0f);
        panel.title.transform.localRotation = panel.paper.localRotation;
        panel.title.transform.localPosition = textTop;
        panel.memo.transform.localRotation = panel.paper.localRotation;
        // 献立名(と「完成！」)の行のぶん下から
        panel.memo.transform.localPosition = textTop + panel.paper.localRotation * new Vector3(0f, -lineSize * 6.4f * (titleLines + extra), 0f);

        // 食材のアイコン(メモの反対側に並べる)
        float iconAreaMin = memoLeft ? -view.x / 2f + memoAreaW : -view.x / 2f;
        float iconAreaW = view.x - memoAreaW;
        int n = panel.icons.Length;
        float slot = iconAreaW / Mathf.Max(1, n);
        float iconSize = Mathf.Min(slot * 0.8f, bandH * 0.75f);
        Color shadow = Color.Lerp(bandColor, Color.white, 0.45f);
        shadow.a = 0.9f;
        for (int k = 0; k < n; k++)
        {
            var sr = panel.icons[k];
            string ing = currentMenu.ingredients[k];
            bool got = p.completedMenu != null || names.Contains(ing);
            sr.transform.localPosition = new Vector3(iconAreaMin + slot * (k + 0.5f), bottom + bandH / 2f, 0f);
            Vector2 size = sr.sprite.bounds.size;
            float fit = iconSize / Mathf.Max(0.01f, Mathf.Max(size.x, size.y));
            bool hasImage = panel.iconNames[k] == null;
            if (hasImage)
            {
                // 絵のまわりの透明な余白を除いた、見えている所の大きさで合わせる(余白の多い絵が小さく見えないように)。
                // 見えている所の真ん中を、枠の真ん中に置く。縦横の長いほうが iconSize なので、枠からははみ出さない
                var vis = VisibleBounds(sr.sprite);
                fit = iconSize / Mathf.Max(0.01f, Mathf.Max(vis.size.x, vis.size.y));
                sr.transform.localScale = new Vector3(fit, fit, 1f);
                sr.transform.localPosition -= new Vector3(vis.center.x * fit, vis.center.y * fit, 0f);
                sr.sharedMaterial = got ? spriteMaterial : panel.silhouettes[k];
                sr.color = got ? Color.white : shadow;
            }
            else
            {
                // 画像が無い食材: 名前の札(入ったら白い札・まだなら薄い札)
                sr.transform.localScale = new Vector3(iconSize, iconSize * 0.6f, 1f);
                sr.color = got ? Color.white : new Color(1f, 1f, 1f, 0.3f);
                var tm = panel.iconNames[k];
                tm.transform.localPosition = sr.transform.localPosition;
                FitTextWidth(tm, iconSize * 0.2f / 6.4f * 1.6f, iconSize * 0.9f);   // 長い名前は札の幅に収まるまで小さく
                tm.color = got ? new Color(0.15f, 0.15f, 0.15f) : new Color(1f, 1f, 1f, 0.6f);
            }
        }
    }

    // 文字の横幅(ゲームの長さ)。フォントの文字の幅を足して計算する(1行の高さ ≒ 文字の大きさ(ピクセル)なので、1ピクセル = characterSize x 0.1)
    static float TextWidth(TextMesh tm, string text)
    {
        var font = tm.font;
        if (font == null || string.IsNullOrEmpty(text)) return 0f;
        font.RequestCharactersInTexture(text, tm.fontSize, tm.fontStyle);
        float px = 0f;
        foreach (char ch in text)
            if (font.GetCharacterInfo(ch, out var info, tm.fontSize, tm.fontStyle)) px += info.advance;
        return px * tm.characterSize * 0.1f;
    }

    // text を、横幅 maxWidth に収まるように何行かに分ける(今の文字の大きさで)。2行のときは、2行の長さがなるべくそろう所で分ける
    static List<string> WrapText(TextMesh tm, string text, float maxWidth)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) { lines.Add(""); return lines; }
        if (TextWidth(tm, text) <= maxWidth) { lines.Add(text); return lines; }
        // 前から詰めて、はみ出す手前で次の行へ
        int start = 0;
        for (int end = 1; end <= text.Length; end++)
        {
            if (end - start > 1 && TextWidth(tm, text.Substring(start, end - start)) > maxWidth)
            {
                lines.Add(text.Substring(start, end - 1 - start));
                start = end - 1;
            }
        }
        lines.Add(text.Substring(start));
        // 2行なら、長いほうの行が一番短くなる所で分け直す(「海鮮丼のなんとか」+「丼」のように1文字だけ残らないように)
        if (lines.Count == 2)
        {
            int best = lines[0].Length;
            float bestW = float.MaxValue;
            for (int cut = 1; cut < text.Length; cut++)
            {
                float a = TextWidth(tm, text.Substring(0, cut)), b = TextWidth(tm, text.Substring(cut));
                if (a > maxWidth || b > maxWidth) continue;
                if (Mathf.Max(a, b) < bestW) { bestW = Mathf.Max(a, b); best = cut; }
            }
            lines[0] = text.Substring(0, best);
            lines[1] = text.Substring(best);
        }
        return lines;
    }

    // 文字を size の大きさにする。ただし横幅が maxWidth を超えるなら、収まる大きさまで小さくする。
    // (今の横幅 ÷ 今の大きさ で「大きさ 1 あたりの横幅」を出して計算する。文字を変えたフレームは横幅がまだ前のままのことがあるが、次のフレームで合う)
    static void FitTextWidth(TextMesh tm, float size, float maxWidth)
    {
        float want = size * 64f / Mathf.Max(1, tm.fontSize);   // SetTextSize と同じ計算
        float now = tm.characterSize;
        // 見た目の範囲(bounds)は傾けた文字を囲む箱なので、傾きの分を取り除いて、文字そのものの横幅にする
        Vector3 box = tm.GetComponent<MeshRenderer>().bounds.size;
        float ang = tm.transform.eulerAngles.z * Mathf.Deg2Rad;
        float cos = Mathf.Abs(Mathf.Cos(ang)), sin = Mathf.Abs(Mathf.Sin(ang));
        float width = cos * cos - sin * sin > 0.1f ? (box.x * cos - box.y * sin) / (cos * cos - sin * sin) : box.x;
        if (now > 0.0001f && width > 0.0001f)
        {
            float perSize = width / now;
            want = Mathf.Min(want, maxWidth / perSize);
        }
        tm.characterSize = want;
    }

    // 絵の、見えている所(透明でない所)の範囲(絵の中の位置。ピボットが 0)。絵ごとに1回だけ数える。
    // 絵の網目(sprite.vertices)を使う: 取り込みの Mesh Type が Tight(初め)なら、網目は見えている所をぴったり囲んでいる。
    // (前はピクセルを読んでいたが、読めない設定の絵で透明な所が正しく読めず、位置・大きさがおかしくなった)
    readonly Dictionary<Sprite, Bounds> visibleBoundsCache = new Dictionary<Sprite, Bounds>();

    Bounds VisibleBounds(Sprite sprite)
    {
        if (visibleBoundsCache.TryGetValue(sprite, out var cached)) return cached;
        Bounds result = sprite.bounds;
        var v = sprite.vertices;
        if (v != null && v.Length > 0)
        {
            Vector2 min = v[0], max = v[0];
            foreach (var p in v) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            // 小さすぎる(読み違い)ときは、絵の全体のまま
            if ((max - min).x > sprite.bounds.size.x * 0.05f && (max - min).y > sprite.bounds.size.y * 0.05f)
                result = new Bounds((min + max) / 2f, max - min);
        }
        visibleBoundsCache[sprite] = result;
        return result;
    }

    MenuPanel BuildMenuPanel(Player p)
    {
        var panel = new MenuPanel { builtFor = currentMenu };
        panel.root = new GameObject($"MenuPanel_{p.name}").transform;
        panel.root.gameObject.layer = p.layer;
        panel.band = MakeBagPart("Band", panel.root, p.layer, p.color, 1600, false);
        panel.paper = MakeBagPart("Paper", panel.root, p.layer, Color.white, 1620, false);
        panel.title = MakeText("MemoTitle", panel.root, p.layer, "", new Color(0.15f, 0.15f, 0.15f), 1625);
        panel.title.anchor = TextAnchor.UpperLeft;
        panel.title.alignment = TextAlignment.Left;
        panel.title.richText = true;
        panel.memo = MakeText("Memo", panel.root, p.layer, "", new Color(0.15f, 0.15f, 0.15f), 1625);
        panel.memo.anchor = TextAnchor.UpperLeft;
        panel.memo.alignment = TextAlignment.Left;
        panel.memo.richText = true;
        // 形だけ1色で塗るマテリアル(文字と同じ描き方: 絵の透明なところだけ使い、色は SpriteRenderer の色)
        if (silhouetteMaterial == null) silhouetteMaterial = new Material(labelFont.material.shader) { name = "MenuSilhouette" };

        int n = currentMenu.ingredients.Length;
        panel.icons = new SpriteRenderer[n];
        panel.iconNames = new TextMesh[n];
        panel.silhouettes = new Material[n];
        for (int k = 0; k < n; k++)
        {
            string ing = currentMenu.ingredients[k];
            // 商品の画像。画像が無く3Dモデルがある商品は、モデルを撮影した画像(ModelIconSprite)
            Sprite sprite = null;
            if (productList != null)
                foreach (var info in productList)
                {
                    if (info == null || info.name != ing) continue;
                    if (info.sprite != null) { sprite = info.sprite; break; }
                    if (info.model != null) { sprite = ModelIconSprite(info); if (sprite != null) break; }
                }
            var icon = MakeBagPart($"Icon_{ing}", panel.root, p.layer, Color.white, 1610, false);
            var sr = icon.GetComponent<SpriteRenderer>();
            if (spriteMaterial == null) spriteMaterial = sr.sharedMaterial;
            if (sprite != null)
            {
                sr.sprite = sprite;
                // シルエットのマテリアルは、アイコンごとに別にして、その画像を持たせる
                // (1つを共有すると、最後に設定した画像が全部のアイコンに使われてしまった)
                panel.silhouettes[k] = new Material(silhouetteMaterial) { mainTexture = sprite.texture };
            }
            else
            {
                var tm = MakeText($"IconName_{ing}", panel.root, p.layer, ing, Color.white, 1611);
                panel.iconNames[k] = tm;
            }
            panel.icons[k] = sr;
        }
        return panel;
    }

    // 3Dモデルの商品のアイコン: モデルを、ゲームと同じ向き(正面)から撮影して画像にする(モデルと向きごとに1回だけ)。
    // どのモデルも縦横の長いほうが画像の枠いっぱい(少しゆとり)に収まるので、アイコンの大きさがそろう。背景は透明
    const int ModelIconLayer = 31;     // 撮影するときだけ使うレイヤー(ほかのカメラには映らない)
    const int ModelIconSize = 256;     // 撮影する画像の大きさ(ピクセル)
    readonly Dictionary<string, Sprite> modelIcons = new Dictionary<string, Sprite>();

    Sprite ModelIconSprite(ProductInfo info)
    {
        string key = $"{info.model.GetInstanceID()}_{info.modelRotation}";
        if (modelIcons.TryGetValue(key, out var cached) && cached != null) return cached;

        // 誰にも見えない場所にモデルを置く(ゲームの商品と同じ向き)
        var model = Instantiate(info.model, new Vector3(0f, -1000f, 0f), Quaternion.Euler(info.modelRotation) * info.model.transform.localRotation);
        SetLayer(model, ModelIconLayer);
        var renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) { Destroy(model); return null; }
        Bounds b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);

        // 正面(ゲームのカメラと同じ -Z の側)から、縦横の長いほうがちょうど収まるように撮る
        var camGo = new GameObject("ModelIconCamera");
        var cam = camGo.AddComponent<Camera>();
        cam.enabled = false;
        cam.orthographic = true;
        cam.orthographicSize = Mathf.Max(b.extents.x, b.extents.y, 0.01f) * 1.08f;
        cam.cullingMask = 1 << ModelIconLayer;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);   // 背景は透明
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = b.size.z + 20f;
        cam.transform.position = new Vector3(b.center.x, b.center.y, b.min.z - 10f);
        cam.transform.rotation = Quaternion.identity;

        var rt = RenderTexture.GetTemporary(ModelIconSize, ModelIconSize, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(ModelIconSize, ModelIconSize, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, ModelIconSize, ModelIconSize), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        cam.targetTexture = null;
        RenderTexture.ReleaseTemporary(rt);
        // 消すのはフレームの終わりなので、その前に見えなくしておく(同じフレームで次のモデルを撮ると、重なって写ってしまうため)
        model.SetActive(false);
        Destroy(camGo);
        Destroy(model);

        var sprite = Sprite.Create(tex, new Rect(0, 0, ModelIconSize, ModelIconSize), new Vector2(0.5f, 0.5f), ModelIconSize);
        modelIcons[key] = sprite;
        return sprite;
    }

    // 「○○完成！ これから1.2倍」を、その人の画面の真ん中に出す
    System.Collections.IEnumerator MenuCompletePopup(Player p, string menuName)
    {
        Vector2 view = PlayerViewSize();
        var label = MakeText("MenuComplete", null, p.layer, $"{menuName}\n完成！\nこれから {menuBonusRate:0.##}倍", new Color(1f, 0.85f, 0.2f), 2500);
        float size = view.x / 70f;
        float duration = Mathf.Max(0.1f, menuCompleteTime);
        for (float time = 0f; time < duration; time += Time.deltaTime)
        {
            float k = time / duration;
            SetTextSize(label, size * (k < 0.1f ? Mathf.Lerp(1.5f, 1f, k / 0.1f) : 1f));   // 最初だけ大きく出て、すぐ元の大きさ
            label.transform.position = new Vector3(0f, view.y * 0.15f, 0f);
            var c = label.color;
            label.color = new Color(c.r, c.g, c.b, k < 0.8f ? 1f : (1f - k) / 0.2f);   // 最後に消えていく
            yield return null;
        }
        Destroy(label.gameObject);
    }
}
