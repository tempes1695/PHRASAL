using UnityEngine;

public class QuizOption : MonoBehaviour {
    bool isCorrect;
    System.Action<bool> onAnswer;

    // 월드 상에 간단한 텍스트 표시용
    public void SetLabel(string text, Font font, int fontSize = 42) {
        var label = new GameObject("Label");
        label.transform.SetParent(transform, false);
        var tm = label.AddComponent<TextMesh>();
        tm.text = text;
        tm.fontSize = fontSize;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.characterSize = 0.06f;
        tm.font = font;
        tm.color = Color.black;

        // **추가: 텍스트가 타일 위에 보이도록 정렬 올리기**
        var mr = tm.GetComponent<MeshRenderer>();
        mr.sortingLayerName = "Default";
        mr.sortingOrder = 10;   // 타일(SpriteRenderer)의 sortingOrder(2)보다 크게!

        label.transform.localPosition = new Vector3(0, 0, -0.1f);
    }


    public void Init(bool correct, System.Action<bool> onAnswerCb) {
        isCorrect = correct;
        onAnswer = onAnswerCb;
    }

    void OnTriggerEnter2D(Collider2D other) {
        if (other.CompareTag("Player")) {
            onAnswer?.Invoke(isCorrect);
        }
    }
}
