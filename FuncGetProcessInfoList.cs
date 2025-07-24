using System;
using System.Collections.Generic;
using System.Data;
using UnityEngine;


// 보내지는 데이터를 처리하는 클래스
public class ProcessInfoRequest
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
public class ProcessInfoResponse
{
    public int TotalCount { get; set; }
    public List<ProcessInfo> ListData { get; set; } = new List<ProcessInfo>();

    public void ByteToData(byte[] bytes, ref int index)
    {
        TotalCount = TCPConverter.ToInt(bytes, ref index);
        int size = TCPConverter.ToInt(bytes, ref index);

        for (int i = 0; i < size; i++)
        {
            ProcessInfo data = new ProcessInfo();
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

// 공정 정보를 처리하는 메인 클래스
public class FuncGetProcessInfoList : FuncInfo
{
    public ProcessInfoRequest RequestData { get; set; } = new ProcessInfoRequest();
    public ProcessInfoResponse ResponseData { get; private set; } = new ProcessInfoResponse();

    protected override int GetFuncID()
    {
        return (int)FuncID.PROCESS_INFO_GET; // FuncID에서 공정 정보를 가져오는 ID
    }

    public override bool DBQuery()
    {
        // 전체 개수 조회 쿼리 (삭제되지 않은 것만)
        string countQuery = "SELECT COUNT(*) AS total_count FROM process_info WHERE is_deleted = FALSE";

        DataTable dtCount = Query(countQuery);
        if (dtCount == null || IsNullOrEmpty(dtCount))
        {
            Common.DEBUG("전체 목록 개수 조회 실패");
            return true;
        }
        ResponseData.TotalCount = ToInt(dtCount, "total_count");

        // 각 페이지에서 가져올 데이터 수
        int pageSize = 10;

        // 데이터 페이징 쿼리 (삭제되지 않은 것만)
        string query = $@"
            SELECT * FROM process_info
            WHERE is_deleted = FALSE
            ORDER BY reg_dt DESC
            OFFSET {RequestData.PageIndex * pageSize} ROWS
            FETCH NEXT {pageSize} ROWS ONLY";

        // process_info 테이블에서 공정 리스트 가져오기
        DataTable dtProcess = Query(query);
        if (dtProcess == null || IsNullOrEmpty(dtProcess))
        {
            Common.DEBUG("공정 정보 불러오기 실패");
            return false;
        }

        for (int i = 0; i < dtProcess.Rows.Count; i++)
        {
            ProcessInfo data = new ProcessInfo
            {
                PID = ToInt(dtProcess, "pid", i),
                PROC_NM = ToString(dtProcess, "proc_nm", i),
                PROC_LOC = ToString(dtProcess, "proc_loc", i),
                INPUT_QR = ToString(dtProcess, "input_qr", i),
                OUTPUT_QR = ToString(dtProcess, "output_qr", i),
                REG_DT = (DateTime)dtProcess.Rows[i]["reg_dt"]
            };

            // parameters 처리 추가
            string jsonStr = ToString(dtProcess, "parameters", i);
            if (!string.IsNullOrEmpty(jsonStr))
            {
                var paramList = JsonUtility.FromJson<ProcessParameters>(jsonStr);
                data.PARAMETERS.Clear();
                foreach (var param in paramList.parameters)
                {
                    data.PARAMETERS[param.key] = param.value;
                }
            }

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
