using System.Collections.Generic;
using UnityEngine;

// 献立ミッション(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「献立ミッション」と「機能のON/OFF」→ Menu Mission)
// 1試合に献立は1つだけ。試合の始めに menuList からランダムに1つ選び(2人共通)、1P/2P それぞれの袋の画面に、献立名と食材を出す。
// 袋に入っている食材は ●(緑)、まだのものは ○。食材5品が全部自分の袋に入ったら「○○完成！」。
// それ以降に袋に入れた商品の金額が menuBonusRate 倍(1.2倍)になる。
//  - 食材は商品の一覧の「名前」で見る(献立の食材の名前と、一覧の名前を同じにする)
//  - 完成は1人1回だけ。完成したあとに食材がこぼれても、倍率はそのまま
//  - 表示の場所と大きさは Inspector の Menu Label Position / Menu Label Size(あとで配置を決める)
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
            label.transform.position = new Vector3(view.x * menuLabelPosition.x, view.y * menuLabelPosition.y, 0f);
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
