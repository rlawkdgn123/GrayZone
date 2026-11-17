using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 피드백 데이터의 후보 클립 선택과 일회성 시각 오브젝트 생성을 공통 처리합니다.
/// </summary>
/// <remarks>
/// 행동 타이밍은 이 유틸리티가 결정하지 않습니다. 무기·감염체·표면 시스템이 확정한 타이밍과 위치만 전달합니다.
/// </remarks>
internal static class FeedbackPlaybackUtility
{
    /// <summary>비어 있지 않은 후보 중 하나를 고르되 가능하면 직전 클립의 연속 반복을 피합니다.</summary>
    internal static bool TryPickClip(
        IReadOnlyList<AudioClip> clips,
        ref int lastIndex,
        out AudioClip clip)
    {
        clip = null;
        if (clips == null || clips.Count == 0)
        {
            return false;
        }

        int validCount = 0;
        for (int i = 0; i < clips.Count; i++)
        {
            if (clips[i] != null)
            {
                validCount++;
            }
        }

        if (validCount == 0)
        {
            return false;
        }

        int pick = Random.Range(0, validCount);
        int selectedIndex = -1;
        for (int i = 0; i < clips.Count; i++)
        {
            if (clips[i] == null)
            {
                continue;
            }

            if (pick-- == 0)
            {
                selectedIndex = i;
                break;
            }
        }

        if (validCount > 1 && selectedIndex == lastIndex)
        {
            for (int offset = 1; offset < clips.Count; offset++)
            {
                int candidate = (selectedIndex + offset) % clips.Count;
                if (clips[candidate] != null)
                {
                    selectedIndex = candidate;
                    break;
                }
            }
        }

        if (selectedIndex < 0)
        {
            return false;
        }

        lastIndex = selectedIndex;
        clip = clips[selectedIndex];
        return clip != null;
    }

    /// <summary>지정한 프리팹을 월드 위치에 생성하고 수명이 양수이면 자동 제거합니다.</summary>
    internal static GameObject SpawnPrefab(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        float lifetime,
        Transform parent = null)
    {
        if (prefab == null)
        {
            return null;
        }

        GameObject instance = Object.Instantiate(prefab, position, rotation);
        if (parent != null)
        {
            instance.transform.SetParent(parent, true);
        }

        if (lifetime > 0.0f)
        {
            Object.Destroy(instance, lifetime);
        }

        return instance;
    }

    /// <summary>표면 법선 방향으로 프리팹을 정렬해 생성합니다.</summary>
    internal static GameObject SpawnAligned(
        GameObject prefab,
        Vector3 position,
        Vector3 normal,
        float lifetime,
        float surfaceOffset = 0.002f,
        Transform parent = null)
    {
        Vector3 safeNormal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
        Quaternion rotation = Quaternion.LookRotation(safeNormal);
        return SpawnPrefab(prefab, position + safeNormal * surfaceOffset, rotation, lifetime, parent);
    }

    /// <summary>트레이서 프리팹을 생성하고 LineRenderer가 있으면 실제 발사 구간을 전달합니다.</summary>
    internal static GameObject SpawnTracer(
        GameObject prefab,
        Vector3 start,
        Vector3 end,
        float lifetime)
    {
        Vector3 delta = end - start;
        Quaternion rotation = delta.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(delta.normalized)
            : Quaternion.identity;

        GameObject instance = SpawnPrefab(prefab, start, rotation, lifetime);
        if (instance == null)
        {
            return null;
        }

        ConfigureTracer(instance, start, end);
        return instance;
    }

    /// <summary>재사용한 트레이서 인스턴스에 이번 발사 구간을 적용합니다.</summary>
    internal static void ConfigureTracer(GameObject instance, Vector3 start, Vector3 end)
    {
        if (instance == null)
        {
            return;
        }

        BulletTrailVisual animatedTrail = instance.GetComponentInChildren<BulletTrailVisual>(true);
        if (animatedTrail != null)
        {
            animatedTrail.Configure(start, end);
            return;
        }

        LineRenderer line = instance.GetComponentInChildren<LineRenderer>(true);
        if (line != null)
        {
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
        }

    }

    /// <summary>풀에서 꺼낸 시각 인스턴스의 파티클·트레일·물리 상태를 새 재생용으로 초기화합니다.</summary>
    internal static void RestartPlayback(GameObject instance)
    {
        if (instance == null)
        {
            return;
        }

        ParticleSystem[] particles = instance.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particles.Length; i++)
        {
            particles[i].Clear(true);
            particles[i].Play(true);
        }

        TrailRenderer[] trails = instance.GetComponentsInChildren<TrailRenderer>(true);
        for (int i = 0; i < trails.Length; i++)
        {
            trails[i].Clear();
        }

        Rigidbody[] rigidbodies = instance.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rigidbodies.Length; i++)
        {
            rigidbodies[i].linearVelocity = Vector3.zero;
            rigidbodies[i].angularVelocity = Vector3.zero;
        }
    }
}
