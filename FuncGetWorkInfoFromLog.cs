using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using UnityEngine;

// 메인 클래스: WORK_INFO_GET 작업을 처리
public class FuncGetWorkInfoFromLog : FuncInfo
{
    public WorkInfoRequest RequestData { get; set; } = new WorkInfoRequest();
    public WorkInfoResponse ResponseData { get; private set; } = new WorkInfoResponse();

    // FuncID 정의
    protected override int GetFuncID()
    {
        return (int)FuncID.WORK_INFO_GET_FROM_LOG;
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

            // 1. proc_list에 해당하는 ProcessInfo들을 work_log에서 먼저 조회
            foreach (int procId in ResponseData.WorkInfo.PROCESS_LIST)
            {
                // work_log에서 해당 procId의 가장 최근 기록을 조회
                string logQuery = $@"
                    SELECT *
                    FROM work_log
                    WHERE serial_no = '{RequestData.SerialNo}'
                    AND proc_id = {procId}
                    ORDER BY input_time DESC
                    LIMIT 1";

                DataTable logResult = Query(logQuery);
                
                if (logResult != null && logResult.Rows.Count > 0)
                {
                    // work_log에서 정보를 찾은 경우
                    DataRow logRow = logResult.Rows[0];
                    ProcessInfo processInfo = new ProcessInfo
                    {
                        PID = ToInt(logRow, "proc_id"),
                        PROC_NM = ToString(logRow, "proc_nm"),
                        PROC_LOC = ToString(logRow, "proc_loc"),
                        INPUT_QR = ToString(logRow, "input_qr"),
                        OUTPUT_QR = ToString(logRow, "output_qr"),
                        REG_DT = (DateTime)logRow["input_time"]
                    };

                    // parameters 처리
                    string jsonStr = ToString(logRow, "parameters");
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
                else
                {
                    // work_log에 없는 경우 process_info에서 조회
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
            }

            // 2. work_flow 목록 조회는 그대로 유지
            string progressQuery = $@"
                SELECT *
                FROM work_flow
                WHERE serial_no = '{RequestData.SerialNo}'
                ORDER BY reg_dt DESC";

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
