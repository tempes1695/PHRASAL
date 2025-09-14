using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class VerticalScroller : MonoBehaviour
{
    // ===== 게임 설정 =====
    [Header("게임 속도 · 난이도")]
    public float baseScrollSpeed = 3f;      // 시작 속도(느리게)
    public float maxScrollSpeed = 7f;       // 최고 속도
    public float timeToMaxSpeed = 120f;     // 최고 속도까지 걸리는 시간(초)
    public float moveSpeed = 9f;            // 좌우 이동 속도
    public float laneHalfWidth = 2.5f;      // 좌우 경계(벽 대신 범위 제한)

    [Header("보조 감속(쉬운 모드)")]
    public bool autoSlowNearRow = true;     // 문제 줄 근처 자동 감속
    public float slowWindowY = 4f;          // 플레이어 ~ 다음 줄 Y 거리 임계값
    [Range(0.2f, 1f)] public float slowMultiplier = 0.55f;  // 감속 비율
    public bool holdSlowEnabled = true;     // Shift(PC)/길게 터치 시 감속
    [Range(0.1f, 1f)] public float holdSlowMultiplier = 0.4f;

    [Header("스폰 설정")]
    public float spawnStartY = 8f;          // 카메라 위 몇 지점부터 스폰
    public float spawnGapY = 8f;            // 퀴즈 줄 간 Y 간격
    public int poolSize = 12;               // (미사용) 여유

    [Header("배경")]
    public float bgTileHeight = 12f;        // 배경 타일 높이
    public Color bgColorA = new Color(0.1f, 0.1f, 0.16f);
    public Color bgColorB = new Color(0.12f, 0.12f, 0.2f);

    [Header("배경 이미지(Seamless)")]
    public Sprite bgSprite;
    public bool bgTiled = true;
    public float bgWidth = 6.9f; // 화면보다 살짝 넓게

    // ===== 내부 상태 =====
    Camera cam;
    Transform camFollow;
    Transform player;
    Rigidbody2D playerRb;
    bool alive = true;
    float maxY;
    float nextSpawnY;
    float startTime; // 가속 타이머

    // 배경 타일
    readonly List<Transform> bgTiles = new();

    // UI
    Canvas canvas;
    Text scoreText, titleText, quizVerbText;
    Button restartBtn;
    Image verbPanel;     // 단어 가독용 반투명 패널
    Font builtinFont;    // LegacyRuntime.ttf

    // 입력
    Vector3 dragStartWorld;
    bool dragging;

    [SerializeField] GameObject playerPrefab;

    // 카메라 옵션
    [Header("Camera")]
    [Range(0.05f, 0.95f)]
    public float playerScreenY = 0.20f;
    public float cameraYOffset = 0f;

    // ==== 퀴즈 관련 ====
    PhrasalQuizManager quizMgr;
    readonly List<Transform> activeOptions = new(); // 현재 씬 상의 모든 선택지 타일

    // "플레이어 앞줄" 찾기용 줄 정보(헤더 동기화 & 자동 감속용)
    class RowInfo { public float y; public PhrasalQuestion q; }
    readonly List<RowInfo> upcomingRows = new();

    // 타일(선택지) 비주얼 사이즈
    [Header("퀴즈 타일")]
    public Vector2 optionSize = new Vector2(2.4f, 1.2f);
    public Color optionColor = new Color(0.95f, 0.95f, 0.95f);

    // === 줄 무적 처리(정답 들어가면 그 줄 통과) ===
    readonly HashSet<int> clearedRowKeys = new(); // 처리된 줄 캐시
    float RowClusterThreshold => Mathf.Max(0.5f, optionSize.y * 0.65f);

    void Awake()
    {
        Application.targetFrameRate = 120;

        // 카메라
        cam = Camera.main;
        if (cam == null) {
            var camObj = new GameObject("Main Camera");
            cam = camObj.AddComponent<Camera>();
        }
        cam.orthographic = true;
        cam.orthographicSize = 6f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.tag = "MainCamera";
        cameraYOffset = 0f;

        // 폰트
        builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // 월드 생성
        CreatePlayer();
        CreateCameraFollow();
        CreateBackground();
        CreateUI();

        // 퀴즈 매니저
        var qm = new GameObject("PhrasalQuizManager");
        quizMgr = qm.AddComponent<PhrasalQuizManager>();
        quizMgr.InitDefaultBank();

        // 배경 초기 정렬
        ResetBackgroundTiles();

        float camY = cam.transform.position.y;
        nextSpawnY = camY + spawnStartY;

        // 스타트시 퀴즈 줄 미리 몇 줄 생성
        for (int i = 0; i < 6; i++) SpawnQuizRow();

        startTime = Time.time; // 가속 시작 시각
        UpdateScoreUI();
    }

    void Update()
    {
        if (!alive)
        {
            if (Input.GetKeyDown(KeyCode.Space)) Restart();
            return;
        }

        float dt = Time.deltaTime;

        // ----- 가속 로직: base → max 를 timeToMaxSpeed 동안 서서히 -----
        float t = Mathf.Clamp01((Time.time - startTime) / Mathf.Max(0.01f, timeToMaxSpeed));
        float currentScroll = Mathf.Lerp(baseScrollSpeed, maxScrollSpeed, t);

        // 자동 감속: 다음 줄이 가까우면 천천히
        float dyNext = DistanceToNextRow();
        if (autoSlowNearRow && dyNext >= 0f && dyNext <= slowWindowY)
            currentScroll *= slowMultiplier;

        // 홀드 감속(Shift 키 또는 화면 꾹): 더 천천히
        if (holdSlowEnabled && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) || Input.GetMouseButton(1)))
            currentScroll *= holdSlowMultiplier;

        // 전진
        playerRb.velocity = new Vector2(playerRb.velocity.x, currentScroll);

        // 좌/우 이동(키보드)
        int h = 0;
        if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) h += 1;
        if (Input.GetKey(KeyCode.LeftArrow)  || Input.GetKey(KeyCode.A)) h -= 1;
        if (h != 0)
        {
            float nx = player.position.x + h * moveSpeed * dt;
            nx = Mathf.Clamp(nx, -laneHalfWidth, laneHalfWidth);
            player.position = new Vector3(nx, player.position.y, 0);
        }

        HandleDrag();

        // 카메라 따라가기
        float desiredCamY = player.position.y - ((playerScreenY * 2f - 1f) * cam.orthographicSize);
        cam.transform.position = new Vector3(0, desiredCamY - cameraYOffset, -10);

        // 스폰 루프
        float camTop = cam.transform.position.y + cam.orthographicSize;
        while (nextSpawnY < camTop + 30f) SpawnQuizRow();

        if (player.position.y > maxY) { maxY = player.position.y; UpdateScoreUI(); }

        // 정리 + 헤더 싱크
        CullOldOptions();
        CullOldRows();
        UpdateUpcomingVerbLabel(); // 플레이어 앞줄 verb 표시
    }

    void HandleDrag()
    {
        if (Input.GetMouseButtonDown(0))
        {
            dragging = true;
            dragStartWorld = ScreenToWorld(Input.mousePosition);
        }
        else if (Input.GetMouseButton(0) && dragging)
        {
            var current = ScreenToWorld(Input.mousePosition);
            float dx = current.x - dragStartWorld.x;
            float nx = Mathf.Clamp(player.position.x + dx, -laneHalfWidth, laneHalfWidth);
            player.position = new Vector3(nx, player.position.y, 0);
            dragStartWorld = current;
        }
        else if (Input.GetMouseButtonUp(0))
        {
            dragging = false;
        }
    }

    Vector3 ScreenToWorld(Vector3 screen)
    {
        var w = cam.ScreenToWorldPoint(screen);
        w.z = 0;
        return w;
    }

    void FixedUpdate()
    {
        if (!alive) return;
        // x속도는 0으로 (즉시 이동)
        playerRb.velocity = new Vector2(0, playerRb.velocity.y);
    }

    void CreatePlayer()
    {
        GameObject go;
        if (playerPrefab != null)
        {
            go = Instantiate(playerPrefab);
            go.name = "Player";
        }
        else
        {
            go = new GameObject("Player");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = CreateRectSprite(new Vector2(0.9f, 1.2f), new Color(0.2f, 0.9f, 0.7f));
            var col = go.AddComponent<BoxCollider2D>();
            col.size = sr.sprite.bounds.size;
        }

        player = go.transform;
        player.position = Vector3.zero;

        playerRb = go.GetComponent<Rigidbody2D>();
        if (playerRb == null) playerRb = go.AddComponent<Rigidbody2D>();
        playerRb.gravityScale = 0f;

        var col2d = go.GetComponent<Collider2D>();
        if (col2d == null) col2d = go.AddComponent<BoxCollider2D>();

        go.tag = "Player";
    }

    void CreateCameraFollow()
    {
        var follow = new GameObject("CamFollow");
        camFollow = follow.transform;
        camFollow.position = Vector3.zero;

        cam.transform.SetParent(null, false);
        cam.transform.position = new Vector3(0, 0, -10);
        cam.transform.rotation = Quaternion.identity;
    }

    void CreateBackground()
    {
        for (int i = 0; i < 4; i++)
        {
            var tile = new GameObject("BG_" + i);
            var sr = tile.AddComponent<SpriteRenderer>();
            sr.sortingOrder = -10;

            if (bgSprite == null)
            {
                sr.sprite = CreateRectSprite(new Vector2(bgWidth, bgTileHeight),
                            (i % 2 == 0) ? bgColorA : bgColorB);
            }
            else
            {
                sr.sprite = bgSprite;
                if (sr.sprite.texture.wrapMode != TextureWrapMode.Repeat)
                    sr.sprite.texture.wrapMode = TextureWrapMode.Repeat;
                sr.drawMode = SpriteDrawMode.Tiled;
                sr.size = new Vector2(bgWidth, bgTileHeight);
            }

            tile.transform.position = new Vector3(0, (i - 1) * bgTileHeight, 10);
            tile.AddComponent<BgTile>().Init(this, bgTileHeight);

            bgTiles.Add(tile.transform);
        }
    }

    void ResetBackgroundTiles()
    {
        if (bgTiles.Count == 0) return;
        float camBottom = cam.transform.position.y - cam.orthographicSize;
        for (int i = 0; i < bgTiles.Count; i++)
        {
            float y = camBottom + (i + 0.5f) * bgTileHeight;
            bgTiles[i].position = new Vector3(0, y, 10);
            var sr = bgTiles[i].GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite == null && bgSprite != null)
            {
                sr.sprite = bgSprite;
                sr.drawMode = SpriteDrawMode.Tiled;
                sr.size = new Vector2(bgWidth, bgTileHeight);
            }
        }
    }

    // ====== UI ======
    void CreateUI()
    {
        var cvGo = new GameObject("Canvas");
        canvas = cvGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = cvGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        cvGo.AddComponent<GraphicRaycaster>();

        scoreText   = CreateUIText("ScoreText",   new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -90), 48, TextAnchor.MiddleCenter);
        titleText   = CreateUIText("Title",       new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -30), 52, TextAnchor.MiddleCenter, bold:true, text:"Phrasal Runner");

        // 단어 패널(반투명 배경) + 텍스트
        verbPanel = CreateUIPanel(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -150), new Vector2(1000, 180), new Color(0,0,0,0.35f));
        quizVerbText = CreateUIText("QuizVerb", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -150), 72, TextAnchor.MiddleCenter, bold:true, text:"");
        quizVerbText.color = new Color(1f, 0.96f, 0.2f); // 선명한 노랑

        // 가독성: 윤곽선 + 그림자 + 자동 폰트 크기
        var outline = quizVerbText.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0,0,0,0.9f);
        outline.effectDistance = new Vector2(2, -2);
        var shadow = quizVerbText.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0,0,0,0.6f);
        shadow.effectDistance = new Vector2(1, -1);
        quizVerbText.resizeTextForBestFit = true;
        quizVerbText.resizeTextMinSize = 36;
        quizVerbText.resizeTextMaxSize = 84;

        restartBtn = CreateUIButton("RESTART (Space)", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 140), Restart);
        restartBtn.gameObject.SetActive(false);
    }

    Image CreateUIPanel(Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 size, Color color)
    {
        var go = new GameObject("Panel");
        go.transform.SetParent(canvas.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.anchoredPosition = anchoredPos; rt.sizeDelta = size;
        var img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    Text CreateUIText(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, int fontSize, TextAnchor align, bool bold=false, string text="")
    {
        var go = new GameObject(name);
        go.transform.SetParent(canvas.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(900, 120);

        var t = go.AddComponent<Text>();
        t.font = builtinFont;
        t.fontSize = fontSize;
        t.alignment = align;
        t.text = text;
        t.color = Color.white;
        if (bold) t.fontStyle = FontStyle.Bold;
        return t;
    }

    Button CreateUIButton(string label, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Action onClick)
    {
        var go = new GameObject("Button");
        go.transform.SetParent(canvas.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(600, 150);

        var img = go.AddComponent<Image>();
        img.color = new Color(0.2f, 0.8f, 0.6f);

        var btn = go.AddComponent<Button>();
        btn.onClick.AddListener(() => onClick());

        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(go.transform, false);
        var lrt = labelGo.AddComponent<RectTransform>();
        lrt.anchorMin = new Vector2(0,0); lrt.anchorMax = new Vector2(1,1); lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;

        var txt = labelGo.AddComponent<Text>();
        txt.font = builtinFont;
        txt.fontSize = 44;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.text = label;
        txt.color = Color.black;

        return btn;
    }

    void UpdateScoreUI()
    {
        int score = Mathf.FloorToInt(maxY * 10f);
        scoreText.text = $"SCORE  {score:n0}";
    }

    public void HitObstacle()
    {
        if (!alive) return;
        alive = false;
        playerRb.velocity = Vector2.zero;
        titleText.text = "GAME OVER";
        restartBtn.gameObject.SetActive(true);
    }

    void Restart()
    {
        alive = true;
        maxY = 0;
        player.position = Vector3.zero;
        camFollow.position = Vector3.zero;
        playerRb.velocity = Vector2.zero;
        titleText.text = "Phrasal Runner";
        restartBtn.gameObject.SetActive(false);

        // 기존 옵션 타일 제거
        foreach (var t in activeOptions) if (t) Destroy(t.gameObject);
        activeOptions.Clear();
        upcomingRows.Clear();
        clearedRowKeys.Clear();

        // 카메라 재정렬
        float desiredCamY = player.position.y - ((playerScreenY * 2f - 1f) * cam.orthographicSize);
        cam.transform.position = new Vector3(0, desiredCamY - cameraYOffset, -10);

        ResetBackgroundTiles();

        float camY = cam.transform.position.y;
        nextSpawnY = camY + spawnStartY;
        for (int i = 0; i < 6; i++) SpawnQuizRow();

        startTime = Time.time;
        UpdateScoreUI();
    }

    // ===== 스프라이트 유틸 =====
    Sprite CreateRectSprite(Vector2 size, Color color)
    {
        int w = Mathf.RoundToInt(size.x * 100);
        int h = Mathf.RoundToInt(size.y * 100);
        Texture2D tex = new Texture2D(w, h);
        var px = new Color[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = color;
        tex.SetPixels(px);
        tex.Apply();
        var sp = Sprite.Create(tex, new Rect(0,0,w,h), new Vector2(0.5f,0.5f), 100f);
        return sp;
    }

    // ===== 배경 타일 =====
    class BgTile : MonoBehaviour
    {
        float height;
        VerticalScroller game;

        public void Init(VerticalScroller g, float h) { game = g; height = h; }

        void Update()
        {
            if (game == null) return;
            float camBottom = game.cam.transform.position.y - game.cam.orthographicSize;

            if (transform.position.y + height * 0.5f < camBottom)
                transform.position += new Vector3(0, height * 4f, 0);
        }
    }

    // ====== 퀴즈 줄 스폰 ======
    void SpawnQuizRow()
    {
        var q = quizMgr.GetRandomQuestion();

        // 헤더는 여기서 바꾸지 않는다 → UpdateUpcomingVerbLabel()에서 "앞줄" 기준으로 갱신

        // 3개 레인 x 좌표
        int[] lanes = new[] { -1, 0, 1 };
        Shuffle(lanes); // 보기 3개를 레인에 랜덤 배치

        // meanings[0..2]를 섞인 레인 순서에 맞춰 붙인다.
        for (int i = 0; i < 3; i++)
        {
            int lane = lanes[i];
            string label = q.meanings[i];           // i번째 의미
            bool isCorrect = (i == q.correctIndex); // correctIndex는 meanings 인덱스 기준

            var opt = CreateOptionTile(label, isCorrect);
            float x = lane * laneHalfWidth * 0.8f;
            opt.position = new Vector3(x, nextSpawnY, 0);
            activeOptions.Add(opt);
        }

        // 줄 정보 기록(플레이어 앞줄 탐색/감속/헤더용)
        upcomingRows.Add(new RowInfo { y = nextSpawnY, q = q });

        nextSpawnY += spawnGapY;
    }

    Transform CreateOptionTile(string labelKorean, bool isCorrect)
    {
        var go = new GameObject(isCorrect ? "Option_Correct" : "Option_Wrong");

        // 바탕판
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = CreateRectSprite(optionSize, optionColor);
        sr.sortingOrder = 2;

        // 콜라이더 (Trigger)
        var col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = optionSize;

        // 스크립트
        var opt = go.AddComponent<QuizOption>();
        opt.Init(isCorrect, OnAnswer);
        opt.SetLabel(labelKorean, builtinFont); // 한글 뜻을 타일에 표시

        return go.transform;
    }

    void OnAnswer(bool correct)
    {
        if (correct)
        {
            // ✅ 같은 줄(현재 Y)의 모든 타일 콜라이더를 끄고 통과 상태로 만든다.
            ProtectCurrentRow();
            // 필요하면 여기서 이펙트/사운드 추가
        }
        else
        {
            HitObstacle(); // 오답 → 게임오버
        }
    }

    // 현재 플레이어가 있는 "줄"을 찾아 그 줄의 모든 타일 콜라이더를 끈다.
    void ProtectCurrentRow()
    {
        if (activeOptions.Count == 0) return;

        // 1) 플레이어에 가장 가까운 타일의 y를 그 줄의 y로 간주
        float py = player.position.y;
        float nearestY = float.MaxValue;
        float bestDy = float.MaxValue;

        for (int i = 0; i < activeOptions.Count; i++)
        {
            var t = activeOptions[i];
            if (t == null) continue;
            float dy = Mathf.Abs(t.position.y - py);
            if (dy < bestDy)
            {
                bestDy = dy;
                nearestY = t.position.y;
            }
        }
        if (nearestY == float.MaxValue) return;

        // 이미 처리한 줄이면 스킵
        int key = Mathf.RoundToInt(nearestY * 100f);
        if (!clearedRowKeys.Add(key)) return;

        // 2) 같은 줄(= y가 근접한) 타일들의 콜라이더를 끈다.
        float thr = RowClusterThreshold;
        for (int i = 0; i < activeOptions.Count; i++)
        {
            var t = activeOptions[i];
            if (t == null) continue;
            if (Mathf.Abs(t.position.y - nearestY) <= thr)
            {
                var col = t.GetComponent<Collider2D>();
                if (col != null) col.enabled = false;

                // (선택) 시각 피드백: 살짝 투명하게
                var sr = t.GetComponent<SpriteRenderer>();
                if (sr != null) sr.color = new Color(sr.color.r, sr.color.g, sr.color.b, 0.6f);
            }
        }
    }

    void CullOldOptions()
    {
        float camBottom = cam.transform.position.y - cam.orthographicSize;
        float cutY = camBottom - 20f;

        for (int i = activeOptions.Count - 1; i >= 0; i--)
        {
            var t = activeOptions[i];
            if (t == null) { activeOptions.RemoveAt(i); continue; }
            if (t.position.y < cutY) {
                Destroy(t.gameObject);
                activeOptions.RemoveAt(i);
            }
        }
    }

    void CullOldRows()
    {
        float camBottom = cam.transform.position.y - cam.orthographicSize;
        float cutY = camBottom - 5f; // 살짝 여유
        for (int i = upcomingRows.Count - 1; i >= 0; i--)
        {
            if (upcomingRows[i].y < cutY) upcomingRows.RemoveAt(i);
        }
    }

    void UpdateUpcomingVerbLabel()
    {
        if (quizVerbText == null || upcomingRows.Count == 0) return;

        float py = player.position.y;
        RowInfo best = null;
        float bestDy = float.MaxValue;

        // 플레이어보다 위(y >= py)에 있는 줄 중 가장 가까운 줄 찾기
        for (int i = 0; i < upcomingRows.Count; i++)
        {
            var r = upcomingRows[i];
            if (r.y >= py)
            {
                float dy = r.y - py;
                if (dy < bestDy) { bestDy = dy; best = r; }
            }
        }

        if (best != null && best.q != null)
            quizVerbText.text = best.q.verb;
    }

    float DistanceToNextRow()
    {
        float py = player.position.y;
        float bestDy = -1f;
        for (int i = 0; i < upcomingRows.Count; i++)
        {
            var r = upcomingRows[i];
            if (r.y >= py)
            {
                float dy = r.y - py;
                if (bestDy < 0f || dy < bestDy) bestDy = dy;
            }
        }
        return bestDy; // 없으면 -1
    }

    // 유틸: 배열 셔플
    static void Shuffle<T>(IList<T> list)
    {
        for (int n = list.Count - 1; n > 0; n--)
        {
            int k = UnityEngine.Random.Range(0, n + 1);
            (list[n], list[k]) = (list[k], list[n]);
        }
    }
}
