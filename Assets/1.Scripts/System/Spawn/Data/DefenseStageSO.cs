using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using VInspector;

/// <summary>
/// 방어전 한 판의 구성입니다. 총 웨이브 수와 공격로별 웨이브 SO 목록을 가집니다.
/// </summary>
/// <remarks>
/// DefenseManager가 현재 방어전 SO 하나를 가지고, 다음 방어전에는 이 SO만 교체합니다.
/// 씬의 방어전 스포너는 공격로 이름으로 여기의 목록과 연결됩니다. SO는 씬 오브젝트를 참조할 수 없으므로
/// 이름(문자열)을 씁니다. 레벨이 바뀌어 공격로가 늘거나 줄어도 코드를 고치지 않기 위해서입니다.
///
/// 목록 순서가 실행 순서입니다. 전투가 시작될 때마다 다음 칸으로 넘어가며, 빈 칸이면 그 웨이브에 해당 공격로는
/// 쉽니다. 목록이 총 웨이브 수보다 짧으면 남은 웨이브도 쉽니다. 마지막 칸을 반복하지 않습니다.
/// 설계 근거: privateDoc `DEFENSE_WAVE_SPAWN_SPEC_KR.md` §4.3.
/// </remarks>
[CreateAssetMenu(fileName = "DefenseStage_Name", menuName = "GrayZone/Defense/Defense Stage")]
public sealed class DefenseStageSO : ScriptableObject
{
    /// <summary>공격로 하나와 그 공격로가 웨이브마다 실행할 웨이브 SO 목록입니다.</summary>
    [Serializable]
    public struct Route
    {
        [Tooltip("씬의 방어전 스포너에 지정한 공격로 이름과 같아야 합니다. 예: Bridge, Tunnel. 대소문자를 구분합니다.")]
        [SerializeField] private string m_routeId;

        [Tooltip("웨이브 순서대로 실행할 웨이브 SO입니다. 빈 칸이면 그 웨이브에 이 공격로는 쉽니다.")]
        [SerializeField] private List<DefenseWaveSO> m_waves;

        /// <summary>공격로 이름입니다. 앞뒤 공백은 뺍니다.</summary>
        public string RouteId => m_routeId != null ? m_routeId.Trim() : string.Empty;

        /// <summary>웨이브 순서대로 실행할 웨이브 SO 목록입니다. 빈 칸이 있을 수 있습니다.</summary>
        public IReadOnlyList<DefenseWaveSO> Waves => m_waves ?? (IReadOnlyList<DefenseWaveSO>)Array.Empty<DefenseWaveSO>();
    }

    [Header("Stage")]
    [Tooltip("이 방어전의 웨이브 수입니다. 마지막 웨이브의 적을 모두 처리하면 귀환 포인트가 열립니다.")]
    [Min(1)]
    [SerializeField] private int m_totalWaveCount = 3;

    [Tooltip("이 일차에서 동시에 살아 있을 수 있는 적의 상한입니다. 0이면 DefenseManager의 기본 상한을 사용합니다.")]
    [Min(0)]
    [SerializeField] private int m_maxActiveEnemies;

    [Header("Wave Clear Rewards")]
    [Tooltip("웨이브별 클리어 시 즉시 지급할 함정 재료 수량입니다. 첫 칸이 1웨이브입니다. 빈 칸이나 0은 지급하지 않습니다.")]
    [SerializeField] private List<int> m_waveClearTrapMaterialRewards = new List<int>();

    [Header("Routes")]
    [Tooltip("공격로별 웨이브 SO 목록입니다. 같은 공격로 이름을 두 번 넣지 않습니다.")]
    [SerializeField] private List<Route> m_routes = new List<Route>();

    /// <summary>이 방어전의 웨이브 수입니다.</summary>
    public int TotalWaveCount => Mathf.Max(1, m_totalWaveCount);

    /// <summary>이 일차의 생존 적 상한입니다. 0이면 DefenseManager 기본값을 사용합니다.</summary>
    public int MaxActiveEnemies => Mathf.Max(0, m_maxActiveEnemies);

    /// <summary>지정한 웨이브를 클리어했을 때 즉시 지급할 함정 재료 수량입니다.</summary>
    /// <param name="waveIndex">0부터 시작하는 웨이브 순번입니다.</param>
    public int GetWaveClearTrapMaterialReward(int waveIndex)
    {
        if (waveIndex < 0 || m_waveClearTrapMaterialRewards == null || waveIndex >= m_waveClearTrapMaterialRewards.Count)
        {
            return 0;
        }

        return Mathf.Max(0, m_waveClearTrapMaterialRewards[waveIndex]);
    }

    /// <summary>공격로 목록입니다.</summary>
    public IReadOnlyList<Route> Routes => m_routes;

    /// <summary>웨이브별로 공격로마다 평균 그룹 밸류를 보여줍니다. 에디터 확인용입니다.</summary>
    /// <remarks>공격로 사이 비교용 참고값입니다. 실제 총량은 각 웨이브 SO의 예상 총 밸류를 봅니다.</remarks>
    [ShowInInspector]
    public string PreviewByWave
    {
        get
        {
            var builder = new StringBuilder();
            for (int wave = 0; wave < TotalWaveCount; wave++)
            {
                builder.Append('W').Append(wave + 1).Append(':');
                for (int r = 0; r < m_routes.Count; r++)
                {
                    DefenseWaveSO waveSO = GetWave(r, wave);
                    builder.Append(' ').Append(string.IsNullOrEmpty(m_routes[r].RouteId) ? "(이름 없음)" : m_routes[r].RouteId).Append('=');
                    builder.Append(waveSO != null ? waveSO.AverageGroupValue.ToString("0.#") : "쉼");
                }

                if (wave < TotalWaveCount - 1)
                {
                    builder.Append('\n');
                }
            }

            return builder.ToString();
        }
    }

    /// <summary>
    /// 지정한 공격로가 지정한 웨이브에 실행할 웨이브 SO를 찾습니다.
    /// </summary>
    /// <param name="routeId">공격로 이름입니다.</param>
    /// <param name="waveIndex">0부터 시작하는 웨이브 순번입니다.</param>
    /// <returns>실행할 웨이브 SO입니다. 공격로가 없거나 빈 칸이거나 목록보다 뒤면 null(쉼)입니다.</returns>
    public DefenseWaveSO GetWave(string routeId, int waveIndex)
    {
        int routeIndex = IndexOfRoute(routeId);
        return routeIndex >= 0 ? GetWave(routeIndex, waveIndex) : null;
    }

    /// <summary>지정한 공격로 이름이 이 방어전에 있는지 확인합니다.</summary>
    /// <param name="routeId">공격로 이름입니다.</param>
    public bool HasRoute(string routeId) => IndexOfRoute(routeId) >= 0;

    /// <summary>지정한 공격로의 모든 웨이브에 쓰이는 항목 SO를 집합에 더합니다. 스포너가 풀을 미리 만들 때 씁니다.</summary>
    /// <param name="routeId">공격로 이름입니다.</param>
    /// <param name="entries">항목을 추가할 집합입니다.</param>
    public void CollectEntries(string routeId, HashSet<EnemySpawnEntrySO> entries)
    {
        int routeIndex = IndexOfRoute(routeId);
        if (routeIndex < 0)
        {
            return;
        }

        IReadOnlyList<DefenseWaveSO> waves = m_routes[routeIndex].Waves;
        for (int i = 0; i < waves.Count; i++)
        {
            waves[i]?.CollectEntries(entries);
        }
    }

    /// <summary>
    /// 공격로 이름이 비어 있거나 두 번 나오는 문제를 찾아 문구로 돌려줍니다.
    /// </summary>
    /// <param name="problems">찾은 문제를 추가할 목록입니다.</param>
    /// <remarks>DefenseManager가 방어전 시작 시 씬 스포너와의 불일치와 함께 경고로 남깁니다.</remarks>
    public void CollectProblems(List<string> problems)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < m_routes.Count; i++)
        {
            string id = m_routes[i].RouteId;
            if (string.IsNullOrEmpty(id))
            {
                problems.Add($"{name}: {i}번째 공격로의 이름이 비어 있습니다.");
                continue;
            }

            if (!seen.Add(id))
            {
                problems.Add($"{name}: 공격로 이름 '{id}'가 두 번 이상 있습니다. 앞의 것만 씁니다.");
            }
        }
    }

    private DefenseWaveSO GetWave(int routeIndex, int waveIndex)
    {
        if (waveIndex < 0 || waveIndex >= TotalWaveCount)
        {
            return null;
        }

        IReadOnlyList<DefenseWaveSO> waves = m_routes[routeIndex].Waves;
        return waveIndex < waves.Count ? waves[waveIndex] : null;
    }

    private int IndexOfRoute(string routeId)
    {
        if (string.IsNullOrWhiteSpace(routeId))
        {
            return -1;
        }

        string key = routeId.Trim();
        for (int i = 0; i < m_routes.Count; i++)
        {
            if (string.Equals(m_routes[i].RouteId, key, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>입력 하한을 보정합니다.</summary>
    /// <remarks>
    /// 공격로 이름 검사는 여기서 하지 않습니다. 이름을 입력하는 도중에도 불려 경고가 계속 쌓이기 때문입니다.
    /// 검사는 방어전 시작 시 <see cref="CollectProblems"/>로 합니다.
    /// </remarks>
    private void OnValidate()
    {
        m_totalWaveCount = Mathf.Max(1, m_totalWaveCount);
        m_maxActiveEnemies = Mathf.Max(0, m_maxActiveEnemies);

        if (m_waveClearTrapMaterialRewards == null)
        {
            m_waveClearTrapMaterialRewards = new List<int>();
            return;
        }

        for (int i = 0; i < m_waveClearTrapMaterialRewards.Count; i++)
        {
            m_waveClearTrapMaterialRewards[i] = Mathf.Max(0, m_waveClearTrapMaterialRewards[i]);
        }
    }
}
