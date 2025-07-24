using System;
using System.Data;
using UnityEngine;

// 영향도 확인 요청 클래스
public class ProcessArchiveImpactRequest
{
    public int PID { get; set; }

    public void ByteToData(byte[] bytes, ref int index)
    {
        PID = TCPConverter.ToInt(bytes, ref index);
    }

    public void DataToByte(byte[] bytes, ref int index)
    {
        TCPConverter.SetBytes(bytes, PID, ref index);
    }
}

// 영향도 확인 응답 클래스
public class ProcessArchiveImpactResponse
{
    public string ProcessName { get; set; } = "";
    public int NotStartedWorkCount { get; set; } // 시작 전 작업 수 (공정만 제거됨)
    public int InProgressWorkCount { get; set; } // 진행 중 작업 수 (초기화 및 공정 제거됨)
    public int CompletedWorkCount { get; set; }  // 완료된 작업 수 (영향 없음)
    public bool CanArchive { get; set; } = true;

    public void ByteToData(byte[] bytes, ref int index)
    {
        ProcessName = TCPConverter.ToString(bytes, ref index);
        NotStartedWorkCount = TCPConverter.ToInt(bytes, ref index);
        InProgressWorkCount = TCPConverter.ToInt(bytes, ref index);
        CompletedWorkCount = TCPConverter.ToInt(bytes, ref index);
        CanArchive = TCPConverter.ToBool(bytes, ref index);
    }

    public void DataToByte(byte[] bytes, ref int index)
    {
        TCPConverter.SetBytes(bytes, ProcessName, ref index);
        TCPConverter.SetBytes(bytes, NotStartedWorkCount, ref index);
        TCPConverter.SetBytes(bytes, InProgressWorkCount, ref index);
        TCPConverter.SetBytes(bytes, CompletedWorkCount, ref index);
        TCPConverter.SetBytes(bytes, CanArchive, ref index);
    }
}

// 영향도 확인 함수 클래스
public class FuncGetProcessArchiveImpact : FuncInfo
{
    public ProcessArchiveImpactRequest RequestData { get; set; } = new ProcessArchiveImpactRequest();
    public ProcessArchiveImpactResponse ResponseData { get; private set; } = new ProcessArchiveImpactResponse();

    protected override int GetFuncID()
    {
        return (int)FuncID.PROCESS_DELETE_IMPACT; // 기존 ID 재활용
    }

    public override bool DBQuery()
    {
        try
        {
            string query = $@"
                WITH AffectedWork AS (
                    SELECT 
                        wi.serial_no,
                        (SELECT COUNT(*) FROM work_flow WHERE serial_no = wi.serial_no AND status = 1) as completed_processes,
                        array_length(wi.proc_list, 1) as total_processes,
                        EXISTS (SELECT 1 FROM work_flow WHERE serial_no = wi.serial_no) as has_started
                    FROM work_info wi
                    WHERE {RequestData.PID} = ANY(wi.proc_list)
                )
                SELECT
                    (SELECT proc_nm FROM process_info WHERE pid = {RequestData.PID}) as proc_nm,
                    (SELECT COUNT(*) FROM AffectedWork WHERE has_started = false) as not_started_count,
                    (SELECT COUNT(*) FROM AffectedWork WHERE has_started = true AND completed_processes < total_processes) as in_progress_count,
                    (SELECT COUNT(*) FROM AffectedWork WHERE completed_processes >= total_processes) as completed_count
            ";
            
            DataTable result = Query(query);
            if (result != null && result.Rows.Count > 0)
            {
                ResponseData.ProcessName = ToString(result.Rows[0], "proc_nm");
                ResponseData.NotStartedWorkCount = ToInt(result.Rows[0], "not_started_count");
                ResponseData.InProgressWorkCount = ToInt(result.Rows[0], "in_progress_count");
                ResponseData.CompletedWorkCount = ToInt(result.Rows[0], "completed_count");
            }
            return true;
        }
        catch (Exception ex)
        {
            Common.DEBUG($"보관 영향도 분석 실패: {ex.Message}");
            return false;
        }
    }

    public override void ByteToData(byte[] bytes)
    {
        int index = 0;
        index += sizeof(int);
        RequestData.ByteToData(bytes, ref index);
        ResponseData.ByteToData(bytes, ref index);
    }

    protected override byte[] DataToByte()
    {
        byte[] bytes = new byte[BUFFER_SIZE];
        int index = 0;
        FUNC_ID = GetFuncID();
        TCPConverter.SetBytes(bytes, FUNC_ID, ref index);
        RequestData.DataToByte(bytes, ref index);
        ResponseData.DataToByte(bytes, ref index);
        return FitBytes(bytes, index);
    }
}


// 보내지는 데이터를 처리하는 클래스
public class ProcessArchiveRequest
{
    public int PID { get; set; } 

    public void ByteToData(byte[] bytes, ref int index)
    {
        PID = TCPConverter.ToInt(bytes, ref index);
    }

    public void DataToByte(byte[] bytes, ref int index)
    {
        TCPConverter.SetBytes(bytes, PID, ref index);
    }
}

// 받는 데이터를 처리하는 클래스
public class ProcessArchiveResponse
{
    public bool IsSuccess { get; set; }
    public int ResetWorkCount { get; set; }
    public string ErrorMessage { get; set; } = "";

    public void ByteToData(byte[] bytes, ref int index)
    {
        IsSuccess = TCPConverter.ToBool(bytes, ref index);
        ResetWorkCount = TCPConverter.ToInt(bytes, ref index);
        ErrorMessage = TCPConverter.ToString(bytes, ref index);
    }

    public void DataToByte(byte[] bytes, ref int index)
    {
        TCPConverter.SetBytes(bytes, IsSuccess, ref index);
        TCPConverter.SetBytes(bytes, ResetWorkCount, ref index);
        TCPConverter.SetBytes(bytes, ErrorMessage, ref index);
    }
}


// 공정 보관(논리적삭제)을 처리하는 메인 클래스
public class FuncArchiveProcessInfo : FuncInfo
{
    public ProcessArchiveRequest RequestData { get; set; } = new ProcessArchiveRequest();
    public ProcessArchiveResponse ResponseData { get; private set; } = new ProcessArchiveResponse();

    protected override int GetFuncID()
    {
        return (int)FuncID.PROCESS_INFO_DELETE; // 기존 ID 재활용
    }

    public override bool DBQuery()
    {
        try
        {
            // 1. 초기화 대상(미완료) 작업들의 serial_no 조회
            string findIncompleteQuery = $@"
                SELECT T.serial_no
                FROM (
                    SELECT 
                        wi.serial_no, 
                        (SELECT COUNT(*) FROM work_flow WHERE serial_no = wi.serial_no AND status = 1) as completed_count,
                        array_length(wi.proc_list, 1) as total_count
                    FROM work_info wi
                    WHERE {RequestData.PID} = ANY(wi.proc_list)
                ) AS T
                WHERE T.completed_count < T.total_count";

            DataTable incompleteWorks = Query(findIncompleteQuery);
            if (incompleteWorks != null && incompleteWorks.Rows.Count > 0)
            {
                var serials = new System.Collections.Generic.List<string>();
                foreach(DataRow row in incompleteWorks.Rows)
                {
                    serials.Add($"'{row["serial_no"]}'");
                }
                string serials_str = string.Join(",", serials);

                // 2. 해당 작업들의 진행기록(work_flow) 삭제
                string deleteFlowQuery = $"DELETE FROM work_flow WHERE serial_no IN ({serials_str})";
                Query(deleteFlowQuery);

                // 3. 해당 작업들의 공정 리스트(proc_list)에서 공정 제거
                string updateWorkInfoQuery = $@"
                    UPDATE work_info
                    SET proc_list = array_remove(proc_list, {RequestData.PID})
                    WHERE serial_no IN ({serials_str})";
                Query(updateWorkInfoQuery);

                ResponseData.ResetWorkCount = incompleteWorks.Rows.Count;
            }

            // 4. 공정을 논리적으로 삭제 (보관 처리)
            string archiveQuery = $@"
                UPDATE process_info
                SET is_deleted = TRUE, deleted_at = CURRENT_TIMESTAMP
                WHERE pid = {RequestData.PID}";
            Query(archiveQuery);

            ResponseData.IsSuccess = true;
            Common.DEBUG($"공정 보관 성공: PID = {RequestData.PID}, {ResponseData.ResetWorkCount}개 작업 초기화");
            return true;
        }
        catch (Exception ex)
        {
            Common.DEBUG($"공정 보관 실패: {ex.Message}");
            ResponseData.IsSuccess = false;
            ResponseData.ErrorMessage = ex.Message;
            return false;
        }
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
