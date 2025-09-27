using UnityEngine;
using UnityEngine.UI; // 引入UGUI命名空间
using TMPro;

public class TypeCheck : MonoBehaviour
{
    public TextMeshProUGUI TextWidget;
    public string log;

    void Start()
    {

    }

    void Update()
    {

    }
    void OnValidate()
    {
        if (TextWidget != null)
        {
            TextWidget.text = log;
        }
    }
}
