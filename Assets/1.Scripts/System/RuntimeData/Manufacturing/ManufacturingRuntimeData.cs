using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 제조 시설의 모든 활성 슬롯 작업을 소유하는 셸터 런타임 데이터입니다.
/// </summary>
[Serializable]
public sealed class ManufacturingRuntimeData
{
    public const int SlotCount = 4;

    [SerializeField] private List<ManufacturingJobRuntimeData> m_jobs = new();

    public IReadOnlyList<ManufacturingJobRuntimeData> Jobs
    {
        get
        {
            EnsureValid();
            return m_jobs;
        }
    }

    public int ActiveJobCount => Jobs.Count;

    public bool TryGetJob(int slotIndex, out ManufacturingJobRuntimeData job)
    {
        EnsureValid();
        job = null;
        if (slotIndex < 0)
            return false;

        job = m_jobs.Find(item => item.SlotIndex == slotIndex);
        return job != null;
    }

    /// <summary>
    /// 비어 있는 슬롯에 작업 복제본을 추가합니다. 시설 레벨과 슬롯 해금 검증은 ManufacturingManager가 담당합니다.
    /// </summary>
    public bool TryAddJob(ManufacturingJobRuntimeData job)
    {
        EnsureValid();
        if (job == null)
            return false;

        job.EnsureValid();
        if (!job.IsValid || TryGetJob(job.SlotIndex, out _))
            return false;

        m_jobs.Add(job.Clone());
        m_jobs.Sort(CompareBySlotIndex);
        return true;
    }

    public bool RemoveJob(int slotIndex)
    {
        if (!TryGetJob(slotIndex, out ManufacturingJobRuntimeData job))
            return false;

        return m_jobs.Remove(job);
    }

    public void Clear()
    {
        m_jobs ??= new List<ManufacturingJobRuntimeData>();
        m_jobs.Clear();
    }

    public ManufacturingRuntimeData Clone()
    {
        EnsureValid();
        ManufacturingRuntimeData clone = new ManufacturingRuntimeData();
        for (int i = 0; i < m_jobs.Count; i++)
            clone.m_jobs.Add(m_jobs[i].Clone());

        return clone;
    }

    public void CopyFrom(ManufacturingRuntimeData source)
    {
        if (source == null || ReferenceEquals(source, this))
            return;

        source.EnsureValid();
        m_jobs = new List<ManufacturingJobRuntimeData>(source.m_jobs.Count);
        for (int i = 0; i < source.m_jobs.Count; i++)
            m_jobs.Add(source.m_jobs[i].Clone());

        EnsureValid();
    }

    public void EnsureValid()
    {
        m_jobs ??= new List<ManufacturingJobRuntimeData>();

        HashSet<int> occupiedSlots = new();
        for (int i = 0; i < m_jobs.Count;)
        {
            ManufacturingJobRuntimeData job = m_jobs[i];
            job?.EnsureValid();
            if (job == null || !job.IsValid || !occupiedSlots.Add(job.SlotIndex))
            {
                m_jobs.RemoveAt(i);
                continue;
            }

            i++;
        }

        m_jobs.Sort(CompareBySlotIndex);
    }

    /// <summary>
    /// 안정적인 순회 순서를 위한 편의 기능 없어도 됨
    /// (대신 조회시 job데이터가 들고있는 인덱스를 기반으로 검색해야함)
    /// </summary>
    private static int CompareBySlotIndex(
        ManufacturingJobRuntimeData left,
        ManufacturingJobRuntimeData right)
    {
        return left.SlotIndex.CompareTo(right.SlotIndex);
    }
}
