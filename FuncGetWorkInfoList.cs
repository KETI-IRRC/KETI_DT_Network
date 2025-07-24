using System;
using System.Collections.Generic;
using System.Data;
using UnityEngine;

// WorkInfoListRequest 클래스: 요청 데이터를 처리
public class WorkInfoListRequest
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

// WorkInfoListResponse 클래스: 응답 데이터를 처리
public class WorkInfoListResponse
{
    public int TotalCount { get; set; }
    public List<WorkInfo> WorkList { get; set; } = new List<WorkInfo>();

    public void ByteToData(byte[] bytes, ref int index)
    {
        TotalCount = TCPConverter.ToInt(bytes, ref index);
        int size = TCPConverter.ToInt(bytes, ref index);

        for (int i = 0; i < size; i++)
        {
            WorkInfo workInfo = new WorkInfo();
            workInfo.ByteToData(bytes, ref index);
            WorkList.Add(workInfo);
        }
    }

    public void DataToByte(byte[] bytes, ref int index)
    {
        TCPConverter.SetBytes(bytes, TotalCount, ref index);
        TCPConverter.SetBytes(bytes, WorkList.Count, ref index);

        foreach (var work in WorkList)
        {
            work.DataToByte(bytes, ref index);
        }
    }
}

// 메인 클래스: WORK_INFO_LIST 작업을 처리
public class FuncGetWorkInfoList : FuncInfo
{
    public WorkInfoListRequest RequestData { get; set; } = new WorkInfoListRequest();
    public WorkInfoListResponse ResponseData { get; private set; } = new WorkInfoListResponse();

    // FuncID 정의
    protected override int GetFuncID()
    {
        return (int)FuncID.WORK_INFO_LIST;
    }

    // 데이터베이스 쿼리 메서드
    public override bool DBQuery()
    {
        try
        {
            // 각 페이지에서 가져올 데이터 수
            int pageSize = 10;
            int offset = RequestData.PageIndex * pageSize;

            // 전체 항목 수를 조회하는 쿼리
            string countQuery = "SELECT COUNT(*) AS total_count FROM work_info";
            DataTable dtCount = Query(countQuery);

            if (dtCount == null || dtCount.Rows.Count == 0)
            {
                Common.DEBUG("전체 작업 수를 조회할 수 없습니다.");
                return false;
            }

            // 전체 작업 수를 저장
            ResponseData.TotalCount = ToInt(dtCount.Rows[0], "total_count");

            // 작업 목록을 가져오는 쿼리 (페이징 적용)
            string query = $@"
                SELECT *
                FROM work_info
                ORDER BY reg_dt DESC
                OFFSET {offset} ROWS
                FETCH NEXT {pageSize} ROWS ONLY";

            DataTable result = Query(query);

            if (result == null || result.Rows.Count == 0)
            {
                Common.DEBUG("작업 목록을 조회할 수 없습니다.");
                return false;
            }

            // 작업 목록을 WorkInfo 객체로 변환하여 ResponseData에 저장
            for (int i = 0; i < result.Rows.Count; i++)
            {
                DataRow row = result.Rows[i];
                WorkInfo workInfo = new WorkInfo
                {
                    SERIAL_NO = ToString(row, "serial_no"),
                    QR_CODE = ToString(row, "qr"),
                    COMP_NM = ToString(row, "comp_nm"),
                    MNG_NM = ToString(row, "mgr_nm"),
                    PROCESS_LIST = ToIntArray(row, "proc_list"),
                    Reg_DT = (DateTime)row["reg_dt"]
                };

                ResponseData.WorkList.Add(workInfo);
            }

            return true;
        }
        catch (Exception ex)
        {
            Common.DEBUG($"DBQuery 실행 중 오류 발생: {ex.Message}");
            return false;
        }
    }

    // ByteToData: TCP로 전달된 데이터를 처리 (Request)
    public override void ByteToData(byte[] bytes)
    {
        int index = 0;

        // FUNC_ID 스킵
        index += sizeof(int);

        // Request 데이터를 처리
        RequestData.ByteToData(bytes, ref index);
    }

    // DataToByte: 처리된 데이터를 TCP로 전송 (Response)
    protected override byte[] DataToByte()
    {
        byte[] bytes = new byte[BUFFER_SIZE];
        int index = 0;

        FUNC_ID = GetFuncID();
        TCPConverter.SetBytes(bytes, FUNC_ID, ref index);

        // Response 데이터를 바이트로 변환하여 전송
        ResponseData.DataToByte(bytes, ref index);

        return FitBytes(bytes, index);
    }
}
