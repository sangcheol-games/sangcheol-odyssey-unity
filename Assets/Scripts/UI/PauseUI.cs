using System;
using SCOdyssey.App;
using SCOdyssey.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SCOdyssey.UI
{
    public class PauseUI : BaseUI
    {

        [SerializeField] private Sprite normalSprite;
        [SerializeField] private Sprite selectedSprite;


        // 버튼 배경으로 쓰는 자식 오브젝트 이름. ContentSizeFitter가 붙어있어 스프라이트에 맞춰 높이가 조정된다.
        private const string ButtonBGName = "ButtonBG";

        private static readonly int ButtonCount = Enum.GetNames(typeof(Buttons)).Length;

        // 현재 포커스된 버튼 인덱스 (Buttons enum과 동일한 순서)
        private int _focusIndex;


        private enum Buttons
        {
            Btn_Resume,
            Btn_Retry,
            Btn_Quit
        }


        protected override void Awake()
        {
            base.Awake();

            BindButton(typeof(Buttons));

            GetButton((int)Buttons.Btn_Resume).onClick.AddListener(OnClickResumeButton);
            GetButton((int)Buttons.Btn_Retry).onClick.AddListener(OnClickRetryButton);
            GetButton((int)Buttons.Btn_Quit).onClick.AddListener(OnClickQuitButton);

            // 마우스 호버로도 포커스가 따라오게 해서 키보드 하이라이트와 어긋나지 않도록 한다
            for (int i = 0; i < ButtonCount; i++)
            {
                int index = i;  // 람다가 루프 변수를 공유하지 않도록 복사
                BindEvent(GetButton(index).gameObject, EventTriggerType.PointerEnter, () => SetFocus(index));
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            // UIManager가 인스턴스를 캐싱해 재사용하므로 표시할 때마다 포커스를 초기화한다
            SetFocus((int)Buttons.Btn_Resume);
        }

        // ButtonBG의 ContentSizeFitter가 rect 높이를 스프라이트에 맞춰주므로 sprite만 갈아끼운다.
        private Image GetButtonBG(int index)
        {
            return GetButton(index).transform.Find(ButtonBGName).GetComponent<Image>();
        }

        // 조기 반환(_focusIndex == index면 return)을 넣지 말 것.
        // _focusIndex 초기값이 0이라 OnEnable의 SetFocus(0)이 no-op이 되어 첫 표시 때 하이라이트가 안 걸린다.
        private void SetFocus(int index)
        {
            _focusIndex = index;

            for (int i = 0; i < ButtonCount; i++)
            {
                GetButtonBG(i).sprite = (i == _focusIndex) ? selectedSprite : normalSprite;
            }
        }

        private static int WrapIndex(int index) => ((index % ButtonCount) + ButtonCount) % ButtonCount;

        private void OnClickResumeButton()
        {
            ServiceLocator.Get<IUIManager>().CloseUI(this);
            ServiceLocator.Get<IGameManager>().Resume();
        }

        private void OnClickRetryButton()
        {
            ServiceLocator.Get<IUIManager>().CloseUI(this);
            SceneManager.LoadScene("GameScene");
        }

        private void OnClickQuitButton()
        {
            ServiceLocator.Get<IUIManager>().CloseUI(this);
            SceneManager.LoadScene("MainScene");
        }

        // 상하로 포커스 이동 (양 끝에서 순환). 좌우는 세로 3버튼 메뉴라 무시
        protected override void HandleSelect(Vector2 dir)
        {
            if (dir.y > 0)      SetFocus(WrapIndex(_focusIndex - 1));
            else if (dir.y < 0) SetFocus(WrapIndex(_focusIndex + 1));
        }
        protected override void HandleSubmit() => GetButton(_focusIndex).onClick.Invoke();
        protected override void HandleCancel() => OnClickResumeButton(); // ESC로도 재개
    }
}
