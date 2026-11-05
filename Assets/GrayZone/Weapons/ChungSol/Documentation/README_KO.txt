청솔 전용 샷건 · 게임용 에셋 v44

외형 기준: v43 차콜/레드. 형태·UV·베이크·재질 표현 변경 없음.
권장 가져오기: Unity URP 프로젝트에서 ChungSol_Shotgun_v44_URP.unitypackage를 Import.
프리팹: Assets/GrayZone/Weapons/ChungSol/Prefabs/PF_ChungSol_Shotgun.prefab
검증 환경: Unity 6000.3.17f1 + Universal RP 17.3.0, Linear 색 공간.
URP/Lit을 사용한다. Built-in/HDRP 프로젝트에는 해당 렌더 파이프라인용 재질 변환이 필요하다.

게임용 구조
PF_ChungSol_Shotgun            장착 기준 원점 / 위치 0 / 회전 0 / 배율 1
  Visual                      MeshFilter + MeshRenderer, 보정 회전/스케일 없음
  Sockets
    Socket_Mount              기존 검토 씬의 무기 장착 좌표계
    Socket_Grip_R             기존 오른손 접촉 기준 (Mount와 같은 원점)
    Socket_Grip_L             기존 왼손 접촉 기준
    Socket_ShellInsert        기존 왼쪽 셸 삽입 중심
    Socket_Muzzle             총구 앞면의 중심, 이펙트 부착용

Unity 좌표: +Z 전방, +Y 위, +X 오른쪽. 단위 1 = 1m.
총구 소켓의 +Z는 전방, 셸 삽입 소켓의 +Z는 총기 내부를 향한다.
손 소켓 회전은 무기 좌표계의 기준 방향이다. 손목 뼈용 IK 회전값이 보정된 타깃은 아니다.
기존 검토 씬 WeaponMount에 배치할 때는 프리팹 localPosition=0, localRotation=identity, localScale=1.
실제 게임 캐릭터의 손뼈에 직접 배치할 때의 오프셋과 손가락 겹침 보정은 다음 단계다.

메시 1개 / MeshRenderer 1개 / 재질 1개 / 삼각형 13,716개.
런타임 정점 13,573개: UV·노멀 경계에서 분리된 정점을 포함한 수치.
Unity 메시 .asset에는 기존 FBX의 장착 축 보정을 반영했다. 프리팹에 숨은 축 보정 부모가 없다.
펌프는 기존 합의대로 정적인 메시다. 무기 자체의 Animator·게임플레이 스크립트·충돌체는 포함하지 않는다.
캐릭터, 애니메이션, RealToon, MagicaCloth2, Animation Rigging, 검토 씬에 의존하지 않는다.

재질과 텍스처
T_ChungSol_BaseColor.png       sRGB / BC7 / 2048
T_ChungSol_Normal.png          NormalMap / Non-Color / BC5 / 2048
T_ChungSol_MetalSmooth.png     Non-Color / BC7 / 2048
MetalSmooth: R=금속성, G=AO, B=0, A=매끄러움. AO 강도는 v43과 같은 0.
노멀 강도 1, 매끄러움 배율 1. 텍스처 내부에 v42의 노멀 보정이 이미 반영돼 있다.
Mipmaps On, Trilinear, Anisotropic 4, Clamp. 텍스처 CPU Read/Write Off.

파일 구성
Source/ChungSol_Shotgun_Edit_v44.blend     50개 부품을 유지한 편집본, 텍스처 내장
Source/ChungSol_Shotgun_Export_v44.blend   1개 메시로 합친 출력본, 텍스처 내장
Source/ExtraMaps                         AO, ORM, Surface 등 재작업/다른 도구용 보조 맵
Runtime/SM_ChungSol_Shotgun.fbx           다른 DCC/엔진용 메시·소켓 교환 파일
Runtime/Textures                        게임에 필요한 텍스처 3개
UnityAssets/Assets/GrayZone/...           메타 파일을 포함한 Unity 에셋 폴더

Blender 작업 좌표는 +Y 전방/+Z 위이고, Unity 프리팹은 +Z 전방/+Y 위이다.
FBX 교환본을 별도로 가져올 때는 대상 프로그램의 축 변환 설정을 확인한다.
Unity에서는 제공 프리팹이 축·피벗·재질 연결이 검증된 사용 경로다.
색상/재질만 수정하면 공통 UV를 유지한 채 텍스처를 교체할 수 있다.
형태를 수정했다면 편집본을 기준으로 출력본과 Unity 메시 에셋을 다시 생성해야 한다.

이번 완료 범위: 편집/출력 파일 정리, 독립 Unity 프리팹, 소켓 구성, 전달 패키지.
손 접촉·검지 보정, 실제 게임 코드 연결, 발사/Dragon Breath 이펙트 구현은 포함하지 않는다.
이전 버전과 다른 두 스킨 시안은 원래 폴더에 보존했다.
