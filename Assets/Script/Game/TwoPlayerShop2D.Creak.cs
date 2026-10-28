using UnityEngine;

// 袋がきしむ擬音(TwoPlayerShop2D の続き。設定の欄は TwoPlayerShop2D.cs の「袋がきしむ擬音」と「機能のON/OFF」→ Bag Creak)
// 袋の中の重さが、破れる重さ(Bag Max Weight)の bagCreakStart 割を超えたら、袋のまわりに「ミシ…」などの文字が出る(カイジの「ざわ…」のように)。
// 重くなるほど、たくさん・速く出る。破れる重さを超えたら、赤く大きくなる。ゲーム中から出て、袋を持ち上げている間も出る。
//  - 文字は袋(bag.root)の子にするので、キックの揺れ・持ち上げと一緒に動く
//  - 重さは画面に出さないので、この擬音が「そろそろ破れるかも」の合図になる
public partial class TwoPlayerShop2D
{
    readonly float[] creakTimer = new float[2];

    // LateUpdate から毎フレーム
    void UpdateBagCreak()
    {
        if (!bagCreakEnabled || !bagTearEnabled || p1 == null || InEntry || entryPhase == -1) return;
        if (IsTimeUp && liftDone) return;   // 結果発表になったら止める
        for (int i = 0; i < 2; i++)
        {
            var p = i == 0 ? p1 : p2;
            if (p.bag == null || p.bag.torn) continue;
            float ratio = BagWeight(p) / Mathf.Max(0.01f, bagMaxWeight);   // 1 で破れる重さ
            float start = Mathf.Clamp(bagCreakStart, 0f, 0.99f);
            if (ratio < start) { creakTimer[i] = 0f; continue; }

            creakTimer[i] -= Time.deltaTime;
            if (creakTimer[i] > 0f) continue;
            // 重いほど間が短い(1.6秒 → 破れる重さで 0.35秒。超えたらもっと短い)
            float k = Mathf.Clamp01((ratio - start) / (1f - start));
            creakTimer[i] = ratio > 1f ? 0.2f : Mathf.Lerp(1.6f, 0.35f, k);
            StartCoroutine(CreakWord(p, ratio > 1f, k));
        }
    }

    // 袋の左右のふちのあたりに、擬音を1つ出す(少し浮かんで、ふるえて、消える)
    System.Collections.IEnumerator CreakWord(Player p, bool over, float k)
    {
        if (bagCreakWords == null || bagCreakWords.Length == 0) yield break;
        string word = bagCreakWords[Random.Range(0, bagCreakWords.Length)];
        Color color = over ? new Color(1f, 0.3f, 0.25f) : Color.Lerp(new Color(1f, 1f, 1f, 0.9f), new Color(1f, 0.85f, 0.4f), k);
        var label = MakeText("Creak", p.bag.root, p.layer, word, color, 1400);
        float size = HudTextSize * (over ? 1.1f : Mathf.Lerp(0.6f, 0.9f, k)) * Mathf.Max(0.1f, bagCreakSize);
        SetTextSize(label, size);

        // 場所: 袋のふちのまわり(左右のふちの上のあたり)。文字が画面からはみ出さないように、画面の中に収める
        // (前は袋の外側に出していたので、袋が画面いっぱいだと文字が半分画面の外になった)
        float side = Random.value < 0.5f ? -1f : 1f;
        float halfWidth = word.Length * size * 6.4f / 2f;   // 文字の横幅の半分(およそ。1文字 = 大きさ x 6.4)
        float halfView = PlayerViewSize().x / 2f;
        float x = bagX + side * (bagWidth / 2f + Random.Range(-0.6f, 0.4f));
        x = Mathf.Clamp(x, -halfView + halfWidth + 0.1f, halfView - halfWidth - 0.1f);
        Vector3 basePos = new Vector3(x, floorY + bagHeight * Random.Range(0.75f, 1.1f), 0f);
        label.transform.localRotation = Quaternion.Euler(0f, 0f, side * -Random.Range(5f, 20f));   // 少し傾ける

        const float duration = 1.1f;
        for (float time = 0f; time < duration; time += Time.deltaTime)
        {
            float t = time / duration;
            Vector3 shake = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), 0f) * 0.04f * (over ? 2f : 1f);   // ふるえる
            label.transform.localPosition = basePos + Vector3.up * (t * 0.4f) + shake;
            float a = t < 0.15f ? t / 0.15f : t > 0.7f ? (1f - t) / 0.3f : 1f;   // ふっと出て、ふっと消える
            label.color = new Color(color.r, color.g, color.b, color.a * a);
            yield return null;
        }
        Destroy(label.gameObject);
    }
}
