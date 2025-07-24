using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using UnityEngine;

public class ProgressTracking
{
    public string SERIAL_NO { get; set; }  // 작업 시리얼 넘버
    public int PROC_ID { get; set; }       // 프로세스 ID
    public int STATUS { get; set; }        // 진행 상태 (0: Input, 1: Output)
    public DateTime REG_DT { get; set; }   // 진행된 시간 (등록된 시간)

    // TCP 데이터를 바이트로 변환하는 메서드
    public void DataToByte(byte[] bytes, ref int index)
    {
        TCPConverter.SetBytes(bytes, SERIAL_NO, ref index);
        TCPConverter.SetBytes(bytes, PROC_ID, ref index);
        TCPConverter.SetBytes(bytes, STATUS, ref index);
        TCPConverter.SetBytes(bytes, REG_DT, ref index);
    }

    // TCP로부터 데이터를 받아오는 메서드
    public void ByteToData(byte[] bytes, ref int index)
    {
        SERIAL_NO = TCPConverter.ToString(bytes, ref index);
        PROC_ID = TCPConverter.ToInt(bytes, ref index);
        STATUS = TCPConverter.ToInt(bytes, ref index);
        REG_DT = TCPConverter.ToDateTime(bytes, ref index);
    }
}

// WorkInfoRequest 클래스: 요청 데이터를 처리
public class WorkInfoRequest
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
// WorkInfoResponse 클래스: 응답 데이터를 처리
public class WorkInfoResponse
{
    public WorkInfo WorkInfo { get; set; } = new WorkInfo();  // null일 수 없도록 항상 초기화
    public List<ProcessInfo> ProcessInfoList { get; set; } = new List<ProcessInfo>();  // null일 수 없도록 초기화
    public List<ProgressTracking> ProgressTrackingList { get; set; } = new List<ProgressTracking>();  // null일 수 없도록 초기화

    public void ByteToData(byte[] bytes, ref int index)
    {
        WorkInfo.ByteToData(bytes, ref index);  // 무조건 데이터를 받음

        int processCount = TCPConverter.ToInt(bytes, ref index);
        for (int i = 0; i < processCount; i++)
        {
            ProcessInfo processInfo = new ProcessInfo();
            processInfo.ByteToData(bytes, ref index);
            ProcessInfoList.Add(processInfo);
        }

        int progressCount = TCPConverter.ToInt(bytes, ref index);
        for (int i = 0; i < progressCount; i++)
        {
            ProgressTracking progressTracking = new ProgressTracking();
            progressTracking.ByteToData(bytes, ref index);
            ProgressTrackingList.Add(progressTracking);
        }
    }

    public void DataToByte(byte[] bytes, ref int index)
    {
        // WorkInfo를 무조건 보냄
        WorkInfo.DataToByte(bytes, ref index);

        // ProcessInfoList를 무조건 보냄
        TCPConverter.SetBytes(bytes, ProcessInfoList.Count, ref index);
        foreach (var processInfo in ProcessInfoList)
        {
            processInfo.DataToByte(bytes, ref index);
        }

        // ProgressTrackingList를 무조건 보냄
        TCPConverter.SetBytes(bytes, ProgressTrackingList.Count, ref index);
        foreach (var progressTracking in ProgressTrackingList)
        {
            progressTracking.DataToByte(bytes, ref index);
        }
    }
}
// 메인 클래스: WORK_INFO_GET 작업을 처리
public class FuncGetWorkInfo : FuncInfo
{
    public WorkInfoRequest RequestData { get; set; } = new WorkInfoRequest();
    public WorkInfoResponse ResponseData { get; private set; } = new WorkInfoResponse();

    // FuncID 정의
    protected override int GetFuncID()
    {
        return (int)FuncID.WORK_INFO_GET;
    }

    // 데이터베이스 쿼리 메서드
    public override bool DBQuery()
    {
        try
        {
            // 주어진 시리얼 넘버에 해당하는 작업 정보를 조회하는 쿼리
            string workQuery = $@"
                SELECT *
                FROM work_info
                WHERE serial_no = '{RequestData.SerialNo}'";

            // 데이터베이스 쿼리 실행
            DataTable workResult = Query(workQuery);

            if (workResult == null || workResult.Rows.Count == 0)
            {
                // 결과가 없을 경우, 에러 처리
                Common.DEBUG($"작업 정보를 찾을 수 없습니다: SerialNo = {RequestData.SerialNo}");
                return false;
            }

            // 작업 정보를 객체에 저장
            DataRow workRow = workResult.Rows[0];
            ResponseData.WorkInfo = new WorkInfo
            {
                SERIAL_NO = ToString(workRow, "serial_no"),
                QR_CODE = ToString(workRow, "qr"),
                COMP_NM = ToString(workRow, "comp_nm"),
                MNG_NM = ToString(workRow, "mgr_nm"),
                PROCESS_LIST = ToIntArray(workRow, "proc_list"),
                Reg_DT = (DateTime)workRow["reg_dt"]
            };

            // 1. proc_list에 해당하는 ProcessInfo들을 조회하여 ProcessInfoList에 저장
            foreach (int procId in ResponseData.WorkInfo.PROCESS_LIST)
            {
                string processQuery = $@"
                    SELECT *
                    FROM process_info
                    WHERE pid = {procId}";

                DataTable processResult = Query(processQuery);
                if (processResult != null && processResult.Rows.Count > 0)
                {
                    DataRow processRow = processResult.Rows[0];
                    ProcessInfo processInfo = new ProcessInfo
                    {
                        PID = ToInt(processRow, "pid"),
                        PROC_NM = ToString(processRow, "proc_nm"),
                        PROC_LOC = ToString(processRow, "proc_loc"),
                        INPUT_QR = ToString(processRow, "input_qr"),
                        OUTPUT_QR = ToString(processRow, "output_qr"),
                        REG_DT = (DateTime)processRow["reg_dt"]
                    };

                    // parameters 처리 추가
                    string jsonStr = ToString(processRow, "parameters");
                    if (!string.IsNullOrEmpty(jsonStr))
                    {
                        var paramList = JsonUtility.FromJson<ProcessParameters>(jsonStr);
                        processInfo.PARAMETERS.Clear();
                        foreach (var param in paramList.parameters)
                        {
                            processInfo.PARAMETERS[param.key] = param.value;
                        }
                    }

                    ResponseData.ProcessInfoList.Add(processInfo);
                }
            }

            // 2. 동일한 serial_no의 work_flow 목록을 조회하여 ProgressTrackingList에 저장
            string progressQuery = $@"
                SELECT *
                FROM work_flow
                WHERE serial_no = '{RequestData.SerialNo}'
                ORDER BY reg_dt DESC";  // 등록된 순서대로 조회

            DataTable progressResult = Query(progressQuery);
            if (progressResult != null && progressResult.Rows.Count > 0)
            {
                foreach (DataRow progressRow in progressResult.Rows)
                {
                    ProgressTracking progressTracking = new ProgressTracking
                    {
                        SERIAL_NO = ToString(progressRow, "serial_no"),
                        PROC_ID = ToInt(progressRow, "proc_id"),
                        STATUS = ToInt(progressRow, "status"),
                        REG_DT = (DateTime)progressRow["reg_dt"]
                    };
                    ResponseData.ProgressTrackingList.Add(progressTracking);
                }
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

        // Response 데이터를 처리
        ResponseData.ByteToData(bytes, ref index);
    }

    // DataToByte: 처리된 데이터를 TCP로 전송 (Response)
    protected override byte[] DataToByte()
    {
        byte[] bytes = new byte[BUFFER_SIZE];
        int index = 0;

        FUNC_ID = GetFuncID();
        TCPConverter.SetBytes(bytes, FUNC_ID, ref index);

        RequestData.DataToByte(bytes, ref index);

        // Response 데이터를 바이트로 변환하여 전송
        ResponseData.DataToByte(bytes, ref index);

        return FitBytes(bytes, index);
    }
}
