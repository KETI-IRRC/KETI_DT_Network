using System;
using System.Data;
using UnityEngine;

// WorkInfoDeleteRequest 클래스: 요청 데이터를 처리
public class WorkInfoDeleteRequest
{
    public string SerialNo { get; set; }

    public void ByteToData(byte[] bytes, ref int index)
    {
        SerialNo = TCPConverter.ToString(bytes, ref index);
    }

    public void DataToByte(byte[] bytes, ref int index)
    {
        TCPConverter.SetBytes(bytes, SerialNo, ref index);
    }
}

// WorkInfoDeleteResponse 클래스: 응답 데이터를 처리
public class WorkInfoDeleteResponse
{
    public bool IsSuccess { get; set; }

    public void ByteToData(byte[] bytes, ref int index)
    {
        IsSuccess = TCPConverter.ToBool(bytes, ref index);
    }

    public void DataToByte(byte[] bytes, ref int index)
    {
        TCPConverter.SetBytes(bytes, IsSuccess, ref index);
    }
}

// FuncDeleteWorkInfo 클래스: WORK_INFO_DELETE 작업을 처리
public class FuncDeleteWorkInfo : FuncInfo
{
    public WorkInfoDeleteRequest RequestData { get; set; } = new WorkInfoDeleteRequest();
    public WorkInfoDeleteResponse ResponseData { get; private set; } = new WorkInfoDeleteResponse();

    // FuncID 정의
    protected override int GetFuncID()
    {
        return (int)FuncID.WORK_INFO_DELETE;
    }

    // 데이터베이스 쿼리 메서드
    public override bool DBQuery()
    {
        try
        {
            // 1. work_flow 테이블에서 해당 시리얼 넘버와 관련된 정보를 삭제
            string deleteWorkFlowQuery = $@"
                DELETE FROM work_flow
                WHERE serial_no = '{RequestData.SerialNo}'";
            Query(deleteWorkFlowQuery);

            // 2. work_log 테이블에서 해당 시리얼 넘버와 관련된 정보를 삭제
            string deleteWorkLogQuery = $@"
                DELETE FROM work_log
                WHERE serial_no = '{RequestData.SerialNo}'";
            Query(deleteWorkLogQuery);

            // 3. work_info 테이블에서 해당 시리얼 넘버와 관련된 정보를 삭제
            string deleteWorkInfoQuery = $@"
                DELETE FROM work_info
                WHERE serial_no = '{RequestData.SerialNo}'";
            
            DataTable workInfoResult = Query(deleteWorkInfoQuery);
            if (workInfoResult == null)
            {
                Common.DEBUG($"Work 정보 삭제 실패: SerialNo = {RequestData.SerialNo}");
                ResponseData.IsSuccess = false;
                return true;
            }

            Common.DEBUG($"작업 정보 삭제 성공: SerialNo = {RequestData.SerialNo}");
            ResponseData.IsSuccess = true;
            return true;
        }
        catch (Exception ex)
        {
            Common.DEBUG($"DBQuery 실행 중 오류 발생: {ex.Message}");
            ResponseData.IsSuccess = false;
            return true;
        }
    }

    // ByteToData: TCP로 전달된 데이터를 처리
    public override void ByteToData(byte[] bytes)
    {
        int index = 0;

        // FUNC_ID 스킵
        index += sizeof(int);

        // Request 데이터와 Response 데이터를 모두 처리
        RequestData.ByteToData(bytes, ref index);
        ResponseData.ByteToData(bytes, ref index);
    }

    // DataToByte: 처리된 데이터를 TCP로 전송
    protected override byte[] DataToByte()
    {
        byte[] bytes = new byte[BUFFER_SIZE];
        int index = 0;

        FUNC_ID = GetFuncID();
        TCPConverter.SetBytes(bytes, FUNC_ID, ref index);

        // Request 데이터와 Response 데이터를 모두 바이트로 변환하여 전송
        RequestData.DataToByte(bytes, ref index);
        ResponseData.DataToByte(bytes, ref index);

        return FitBytes(bytes, index);
    }
}
