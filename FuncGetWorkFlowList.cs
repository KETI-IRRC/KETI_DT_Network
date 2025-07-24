using System;
using System.Collections.Generic;
using System.Data;
using UnityEngine;


// 보내지는 데이터를 처리하는 클래스
public class WorkFlowRequest
{
    public int PageIndex { get; set; }

    public void ByteToData(byte[] bytes, ref int index)
    {
        PageIndex = TCPConverter.ToInt(bytes, ref index);
    }

    public void DataToByte(byte[] bytes, ref int index)
    {
        TCPConverter.SetBytes(bytes, PageIndex, ref index);
    }
}

// 받는 데이터를 처리하는 클래스
public class WorkFlowResponse
{
    public int TotalCount { get; set; }
    public List<WorkFlow> ListData { get; set; } = new List<WorkFlow>();

    public void ByteToData(byte[] bytes, ref int index)
    {
        TotalCount = TCPConverter.ToInt(bytes, ref index);
        int size = TCPConverter.ToInt(bytes, ref index);

        for (int i = 0; i < size; i++)
        {
            WorkFlow data = new WorkFlow();
            data.ByteToData(bytes, ref index);
            ListData.Add(data);
        }
    }

    public void DataToByte(byte[] bytes, ref int index)
    {
        TCPConverter.SetBytes(bytes, TotalCount, ref index);
        TCPConverter.SetBytes(bytes, ListData.Count, ref index);

        foreach (var data in ListData)
        {
            data.DataToByte(bytes, ref index);
        }
    }
}

// 작업 목록을 처리하는 메인 클래스
public class FuncGetWorkFlowList : FuncInfo
{
    public WorkFlowRequest RequestData { get; set; } = new WorkFlowRequest();
    public WorkFlowResponse ResponseData { get; private set; } = new WorkFlowResponse();

    protected override int GetFuncID()
    {
        return (int)FuncID.WORK_FLOW_LIST; // FuncID에서 작업 중인 목록 정보를 가져오는 ID
    }

    public override bool DBQuery()
    {
        // 전체 개수 조회 쿼리 - work_info의 모든 항목 수를 가져옴
        string countQuery = @"
            SELECT COUNT(*) AS total_count
            FROM work_info";

        DataTable dtCount = Query(countQuery);
        if (dtCount == null || IsNullOrEmpty(dtCount))
        {
            Common.DEBUG("전체 목록 개수 조회 실패");
            return false;
        }
        ResponseData.TotalCount = ToInt(dtCount, "total_count");

        // 각 페이지에서 가져올 데이터 수
        int pageSize = 10;

        // 데이터 페이징 쿼리 - 모든 work_info와 최신 work_flow 상태를 조합
        string query = $@"
            WITH CompletedCount AS (
                -- 각 작업별로 배출 완료된 공정 수 계산
                SELECT 
                    serial_no,
                    COUNT(*) as completed_count
                FROM work_flow 
                WHERE status = 1  -- 배출 완료된 것만
                GROUP BY serial_no
            ),
            LatestStatus AS (
                -- 각 serial_no별로 가장 최근 work_flow 레코드를 가져옴
                SELECT DISTINCT ON (wf.serial_no)
                    wf.serial_no,
                    wf.proc_id,
                    wf.status,
                    wf.reg_dt,
                    wf.log_id
                FROM work_flow wf
                ORDER BY wf.serial_no, wf.reg_dt DESC
            )
            SELECT 
                wi.serial_no,
                wi.comp_nm,
                wi.mgr_nm,
                wi.proc_list,
                COALESCE(ls.proc_id, -1) AS proc_id,    -- work_flow가 없으면 -1 (작업 대기)
                COALESCE(ls.status, 0) AS status,       -- work_flow가 없으면 0 (기본 상태)
                COALESCE(ls.reg_dt, wi.reg_dt) AS reg_dt,
                ls.log_id,                             -- work_flow가 없으면 NULL
                -- 완료된 공정 수와 전체 공정 수 비교하여 작업 상태 결정
                COALESCE(cc.completed_count, 0) as completed_count,
                array_length(wi.proc_list, 1) as total_count,
                CASE 
                    WHEN COALESCE(cc.completed_count, 0) >= array_length(wi.proc_list, 1) THEN 2  -- 작업 완료
                    WHEN COALESCE(cc.completed_count, 0) > 0 THEN 1  -- 작업 중
                    ELSE 0  -- 작업 대기
                END as work_status,
                CASE 
                    WHEN ls.log_id IS NOT NULL THEN wl.proc_nm  -- 로그가 있으면 로그의 공정명
                    WHEN ls.proc_id IS NOT NULL THEN pi.proc_nm -- 로그는 없지만 공정ID가 있으면 공정정보의 공정명
                    ELSE NULL                                   -- 둘 다 없으면 NULL (작업 대기)
                END as proc_nm,
                CASE 
                    WHEN ls.log_id IS NOT NULL THEN wl.proc_loc  -- 로그가 있으면 로그의 위치정보
                    WHEN ls.proc_id IS NOT NULL THEN pi.proc_loc -- 로그는 없지만 공정ID가 있으면 공정정보의 위치정보
                    ELSE NULL                                    -- 둘 다 없으면 NULL
                END as proc_loc
            FROM work_info wi
            LEFT JOIN CompletedCount cc ON wi.serial_no = cc.serial_no
            LEFT JOIN LatestStatus ls ON wi.serial_no = ls.serial_no
            LEFT JOIN work_log wl ON ls.log_id = wl.log_id
            LEFT JOIN process_info pi ON ls.proc_id = pi.pid
            ORDER BY COALESCE(ls.reg_dt, wi.reg_dt) DESC
            OFFSET {RequestData.PageIndex * pageSize} ROWS
            FETCH NEXT {pageSize} ROWS ONLY";

        DataTable dtProgress = Query(query);
        if (dtProgress == null || IsNullOrEmpty(dtProgress))
        {
            Common.DEBUG("작업 목록 불러오기 실패");
            return false;
        }

        for (int i = 0; i < dtProgress.Rows.Count; i++)
        {
            DataRow row = dtProgress.Rows[i];
            WorkFlow data = new WorkFlow
            {
                SERIAL_NO = ToString(row, "serial_no"),
                COMP_NM = ToString(row, "comp_nm"),
                MNG_NM = ToString(row, "mgr_nm"),
                STATUS = ToInt(row, "status"),
                PID = ToInt(row, "proc_id"),
                PROC_NM = ToString(row, "proc_nm"),
                REG_DT = (DateTime)row["reg_dt"],
                PROCESS_LIST = ToIntArray(row, "proc_list"),
                COMPLETED_COUNT = ToInt(row, "completed_count"),
                TOTAL_COUNT = ToInt(row, "total_count"),
                WORK_STATUS = ToInt(row, "work_status")
            };

            ResponseData.ListData.Add(data);
        }

        return true;
    }

    public override void ByteToData(byte[] bytes)
    {
        int index = 0;

        // FUNC_ID 처리
        index += sizeof(int);

        // Request 데이터 처리
        RequestData.ByteToData(bytes, ref index);

        // Response 데이터 처리
        ResponseData.ByteToData(bytes, ref index);
    }

    protected override byte[] DataToByte()
    {
        byte[] bytes = new byte[BUFFER_SIZE];
        int index = 0;

        FUNC_ID = GetFuncID();
        TCPConverter.SetBytes(bytes, FUNC_ID, ref index);

        // Request 데이터 처리
        RequestData.DataToByte(bytes, ref index);

        // Response 데이터 처리
        ResponseData.DataToByte(bytes, ref index);

        return FitBytes(bytes, index);
    }
}
