using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class PhrasalQuestion {
    public string verb;        // 영어 구동사 (예: "drop by")
    public string[] meanings;  // 보기 3개(한글 뜻). 반드시 길이 3
    public int correctIndex;   // 정답 인덱스(0..2)
}

public class PhrasalQuizManager : MonoBehaviour {
    readonly List<PhrasalQuestion> bank = new();

    public void InitDefaultBank() {
        bank.Clear();

        bank.Add(new PhrasalQuestion {
            verb = "drop by",
            meanings = new [] { "밥먹다", "잠시 들르다", "반성하다" },
            correctIndex = 1
        });

        bank.Add(new PhrasalQuestion {
            verb = "look after",
            meanings = new [] { "돌보다", "뒤돌아보다", "찾아보다" },
            correctIndex = 0
        });

        bank.Add(new PhrasalQuestion {
            verb = "put off",
            meanings = new [] { "연기하다", "벗다", "올려놓다" },
            correctIndex = 0
        });

        bank.Add(new PhrasalQuestion {
            verb = "run into",
            meanings = new [] { "우연히 만나다", "뛰어들다", "달리기 시작하다" },
            correctIndex = 0
        });

        // TODO: 계속 추가!
    }

    public PhrasalQuestion GetRandomQuestion() {
        if (bank.Count == 0) InitDefaultBank();
        return bank[Random.Range(0, bank.Count)];
    }
}
