using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 타이틀 메뉴의 "크레딧" 버튼을 누르면 크레딧 화면을 전체 화면으로 띄우고, "돌아가기" 버튼이나 ESC로 닫습니다.
/// </summary>
/// <remarks>
/// 크레딧 화면은 씬의 Title Canvas 아래에 미리 배치돼 있고(꺼진 상태), 이 컴포넌트는 켜고 끄기만 합니다.
/// 화면 전체를 덮는 크레딧 이미지가 클릭을 받아 뒤의 메뉴 버튼은 눌리지 않습니다.
///
/// 열 때 키보드·패드 선택을 "돌아가기" 버튼으로 옮기고, 닫을 때 "크레딧" 버튼으로 되돌립니다. 선택이 크레딧 버튼에
/// 남아 있으면 Enter를 눌렀을 때 화면이 다시 열리기 때문입니다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class TitleCreditsOverlay : MonoBehaviour
{
    private const string OpenButtonName = "Credits";

    [Tooltip("크레딧 화면 루트입니다. 열면 켜고 닫으면 끕니다.")]
    [SerializeField] private GameObject m_overlayRoot;

    [Tooltip("크레딧 화면을 여는 타이틀 메뉴 버튼입니다. 비어 있으면 씬에서 'Credits'를 찾습니다.")]
    [SerializeField] private Button m_openButton;

    [Tooltip("크레딧 화면의 \"돌아가기\" 버튼입니다.")]
    [SerializeField] private Button m_backButton;

    /// <summary>크레딧 화면이 열려 있는지 여부입니다.</summary>
    public bool IsOpen => m_overlayRoot != null && m_overlayRoot.activeSelf;

    private void Awake()
    {
        if (m_openButton == null)
        {
            m_openButton = FindButton(OpenButtonName);
        }

        if (m_overlayRoot == null)
        {
            Debug.LogWarning($"[TitleCreditsOverlay] '{name}'에 크레딧 화면이 지정되지 않아 크레딧 버튼을 연결하지 않습니다.", this);
            return;
        }

        m_overlayRoot.SetActive(false);
        if (m_openButton != null)
        {
            m_openButton.onClick.RemoveListener(Open);
            m_openButton.onClick.AddListener(Open);
        }

        if (m_backButton != null)
        {
            m_backButton.onClick.RemoveListener(Close);
            m_backButton.onClick.AddListener(Close);
        }
    }

    private void Update()
    {
        if (IsOpen && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Close();
        }
    }

    /// <summary>크레딧 화면을 엽니다.</summary>
    public void Open()
    {
        if (m_overlayRoot == null || IsOpen)
        {
            return;
        }

        m_overlayRoot.SetActive(true);
        Select(m_backButton);
    }

    /// <summary>크레딧 화면을 닫고 타이틀 메뉴로 돌아갑니다.</summary>
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        m_overlayRoot.SetActive(false);
        Select(m_openButton);
    }

    private static void Select(Button button)
    {
        if (button != null && EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(button.gameObject);
        }
    }

    /// <summary>같은 씬의 버튼 가운데 이름이 일치하는 것을 찾습니다.</summary>
    private Button FindButton(string buttonName)
    {
        foreach (Button button in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (button != null && button.gameObject.scene == gameObject.scene && button.name == buttonName)
            {
                return button;
            }
        }

        Debug.LogWarning($"[TitleCreditsOverlay] '{buttonName}' 버튼을 찾지 못해 연결하지 않습니다.", this);
        return null;
    }
}
