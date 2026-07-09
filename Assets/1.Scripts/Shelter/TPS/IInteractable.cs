using UnityEngine;

// 공용 상호작용 인터페이스. 침대/문/작업대/NPC 등 "E로 동작하는" 오브젝트가 구현한다.
// PlayerInteractor가 현재 대상에서 이 인터페이스를 찾아 Interact를 호출한다.
public interface IInteractable
{
    void Interact(GameObject interactor);
}
