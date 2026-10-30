using System.Collections.Generic;
using UnityEngine;

// 献立ミッション(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「献立ミッション」と「機能のON/OFF」→ Menu Mission)
// 1試合に献立は1つだけ。試合の始めに menuList からランダムに1つ選び(2人共通)、1P/2P それぞれの袋の画面に、献立名と食材を出す。
// 袋に入っている食材は ●(緑)、まだのものは ○。食材5品が全部自分の袋に入ったら「○○完成！」。
// それ以降に袋に入れた商品の金額が menuBonusRate 倍(1.2倍)になる。
//  - 食材は商品の一覧の「名前」で見る(献立の食材の名前と、一覧の名前を同じにする)
//  - 完成は1人1回だけ。完成したあとに食材がこぼれても、倍率はそのまま
//  - 表示(menuPanelLayout が ON。2026-10-30 プログラマーの画面イメージ): 袋の画面の下にプレイヤーの色の帯(menuBandHeight)を敷き、
//    食材のアイコン(商品の一覧の画像。まだの食材は灰色のシルエット、袋に入ったら色付き。画像が無い商品は名前の札)と、
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
        new MenuRecipe("えびとブロッコリーのオイルパスタ", "すぱげてぃ", "ゆでえび", "ぶろっこりー", "おりーぶおいる", "ばたー"),
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
        public TextMesh memo;
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
            bool show = menuMission && currentMenu != null && currentMenu.ingredients != null;
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

        // メモの文字(献立名と食材。入ったものは ■)。用紙と一緒に傾ける
        var names = NamesInBag(p);
        var sb = new System.Text.StringBuilder(currentMenu.name);
        if (p.completedMenu != null) sb.Append($" <color=#D08A00>完成！{menuBonusRate:0.##}倍</color>");
        foreach (var ing in currentMenu.ingredients)
        {
            bool got = p.completedMenu != null || names.Contains(ing);
            sb.Append(got ? $"\n<color=#1E9E3A>■ {ing}</color>" : $"\n□ {ing}");
        }
        panel.memo.text = sb.ToString();
        int lines = currentMenu.ingredients.Length + 1;
        SetTextSize(panel.memo, paperH / (lines + 1.5f) / 6.4f);   // 1行の高さ ≒ 大きさ x 6.4
        panel.memo.transform.localRotation = panel.paper.localRotation;
        panel.memo.transform.localPosition = panel.paper.localPosition
            + panel.paper.localRotation * new Vector3(-paperW / 2f + paperW * 0.08f, paperH / 2f - paperH * 0.06f, 0f);

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
                sr.transform.localScale = new Vector3(fit, fit, 1f);
                sr.sharedMaterial = got ? spriteMaterial : silhouetteMaterial;
                sr.color = got ? Color.white : shadow;
            }
            else
            {
                // 画像が無い食材: 名前の札(入ったら白い札・まだなら薄い札)
                sr.transform.localScale = new Vector3(iconSize, iconSize * 0.6f, 1f);
                sr.color = got ? Color.white : new Color(1f, 1f, 1f, 0.3f);
                var tm = panel.iconNames[k];
                tm.transform.localPosition = sr.transform.localPosition;
                SetTextSize(tm, iconSize * 0.2f / 6.4f * 1.6f);
                tm.color = got ? new Color(0.15f, 0.15f, 0.15f) : new Color(1f, 1f, 1f, 0.6f);
            }
        }
    }

    MenuPanel BuildMenuPanel(Player p)
    {
        var panel = new MenuPanel { builtFor = currentMenu };
        panel.root = new GameObject($"MenuPanel_{p.name}").transform;
        panel.root.gameObject.layer = p.layer;
        panel.band = MakeBagPart("Band", panel.root, p.layer, p.color, 1600, false);
        panel.paper = MakeBagPart("Paper", panel.root, p.layer, Color.white, 1620, false);
        panel.memo = MakeText("Memo", panel.root, p.layer, "", new Color(0.15f, 0.15f, 0.15f), 1625);
        panel.memo.anchor = TextAnchor.UpperLeft;
        panel.memo.alignment = TextAlignment.Left;
        panel.memo.richText = true;
        // 形だけ1色で塗るマテリアル(文字と同じ描き方: 絵の透明なところだけ使い、色は SpriteRenderer の色)
        if (silhouetteMaterial == null) silhouetteMaterial = new Material(labelFont.material.shader) { name = "MenuSilhouette" };

        int n = currentMenu.ingredients.Length;
        panel.icons = new SpriteRenderer[n];
        panel.iconNames = new TextMesh[n];
        for (int k = 0; k < n; k++)
        {
            string ing = currentMenu.ingredients[k];
            Sprite sprite = null;
            if (productList != null)
                foreach (var info in productList)
                    if (info != null && info.name == ing && info.sprite != null) { sprite = info.sprite; break; }
            var icon = MakeBagPart($"Icon_{ing}", panel.root, p.layer, Color.white, 1610, false);
            var sr = icon.GetComponent<SpriteRenderer>();
            if (spriteMaterial == null) spriteMaterial = sr.sharedMaterial;
            if (sprite != null) sr.sprite = sprite;
            else
            {
                var tm = MakeText($"IconName_{ing}", panel.root, p.layer, ing, Color.white, 1611);
                panel.iconNames[k] = tm;
            }
            panel.icons[k] = sr;
        }
        return panel;
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
