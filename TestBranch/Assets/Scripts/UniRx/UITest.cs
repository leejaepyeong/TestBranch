using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UniRx;
using TMPro;

public class UITest : MonoBehaviour
{
    public Button buttonScore;
    public TextMeshProUGUI textScore;
    private ReactiveProperty<int> scoreProperty = new ReactiveProperty<int>();

    public int score = 0;
    public float tick = 1;
    public float tickTime = 0;
    private void Awake()
    {
        scoreProperty.Subscribe(x => { score = x; textScore.text = $"{score}"; }).AddTo(textScore); 
    }
    void Start()
    {
        tickTime = 0;
    }
    void Update()
    {
        tickTime += Time.deltaTime;
        if(tickTime > tick)
        {
            scoreProperty.Value += 1;
            tickTime = 0;
        }
    }
}
