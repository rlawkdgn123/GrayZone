/// <summary>
/// 캐릭터가 착용한 신발 종류입니다. 발밑 표면 재질과 조합해 발소리를 선택합니다.
/// </summary>
/// <remarks>
/// Unity 씬과 프리팹에는 정수값으로 직렬화되므로 기존 값은 변경하지 않고 새 값만 뒤에 추가합니다.
/// FMOD의 <c>Footwear</c> 라벨은 이 열거형 이름과 동일하게 유지합니다.
/// </remarks>
public enum FootwearType
{
    /// <summary>신발 종류를 아직 지정하지 않은 상태입니다.</summary>
    Unknown = 0,

    /// <summary>맨발입니다.</summary>
    Barefoot = 10,

    /// <summary>부츠입니다.</summary>
    Boots = 20,

    /// <summary>운동화입니다.</summary>
    Sneakers = 30,

    /// <summary>일반 구두나 슈즈입니다.</summary>
    Shoes = 40,
}
