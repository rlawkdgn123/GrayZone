using UnityEngine;

/// <summary>
/// 이 오브젝트와 자식의 렌더러를 레이어와 상관없이 <see cref="EnvironmentOcclusionCutout"/>의 컷아웃 대상에 넣습니다.
/// </summary>
/// <remarks>
/// 차량, 철조망, 모래주머니처럼 콜라이더가 렌더러와 같은 오브젝트에 있는 물체는 레이어를 Environment로 바꾸면
/// 소음 판정과 NavMesh 수집까지 바뀝니다. 레이어는 그대로 두고 잘라 내기만 할 때 그룹 루트에 붙입니다.
/// 실제로 잘리려면 머티리얼 셰이더가 컷아웃을 지원해야 합니다(URP Lit 머티리얼은 GrayZone/Lit Occlusion Cutout).
/// </remarks>
[DisallowMultipleComponent]
public sealed class OcclusionCutoutTarget : MonoBehaviour
{
}
