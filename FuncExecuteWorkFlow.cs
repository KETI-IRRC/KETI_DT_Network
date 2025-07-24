using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using UnityEngine;

public enum WorkFlowResult
{
    SuccessRecognizeWork,     // 작업 제품 인식에 성공했다는 메시지
    SuccessRecognizeProcess,     // 공정 인식에 성공했다는 메시지
    SuccessCompleted,// 모든 공정을 마친 상태
    ErrorInvalidQR,              // 잘못된 QR 코드를 인식했다는 메시지
    ErrorNoProcessInfo,        // 워크 정보 인식에 성공했다는 메시지
    ErrorNoWorkInfo,             // QR 코드에 해당하는 작업 정보가 없는 경우
}
// Request 클래스를 정의합니다.
public class ExecuteWorkFlowRequest
{
    public string QRCode { get; set; }    // 제품 또는 공정의 QR 코드
    public int PID { get; set; } = -1;          // 다음에 진행할 공정 ID
    public int STATUS { get; set; }        // 다음 공정의 상태 (0: Input, 1: Output)

    public void ByteToData(byte[] bytes, ref int index)
    {
        QRCode = TCPConverter.ToString(bytes, ref index);
        PID = TCPConverter.ToInt(bytes, ref index);
        STATUS = TCPConverter.ToInt(bytes, ref index);
    }

    public void DataToByte(byte[] bytes, ref int index)
    {
        TCPConverter.SetBytes(bytes, QRCode, ref index);
        TCPConverter.SetBytes(bytes, PID, ref index);
        TCPConverter.SetBytes(bytes, STATUS, ref index);
    }
}

// NextProcessInfo 클래스를 정의합니다.
public class NextProcessInfo
{
    public int PID { get; set; }           // 다음에 진행할 공정 ID
    public int STATUS { get; set; }        // 다음 공정의 상태 (0: Input, 1: Output)
    public ProcessInfo ProcessInfo { get; set; } = new ProcessInfo(); // 다음 공정의 ProcessInfo

    public void ByteToData(byte[] bytes, ref int index)
    {
        PID = TCPConverter.ToInt(bytes, ref index);
        STATUS = TCPConverter.ToInt(bytes, ref index);
        ProcessInfo = new ProcessInfo();  // ProcessInfo가 null이더라도 빈 객체를 생성해 받도록 함
        ProcessInfo.ByteToData(bytes, ref index);
    }

    public void DataToByte(byte[] bytes, ref int index)
    {
        TCPConverter.SetBytes(bytes, PID, ref index);
        TCPConverter.SetBytes(bytes, STATUS, ref index);
        ProcessInfo.DataToByte(bytes, ref index);  // 반드시 데이터를 보냄
    }
}

// Response 클래스를 정의합니다.
public class ExecuteWorkFlowResponse
{
    public WorkInfo WorkInfo { get; set; } = new WorkInfo(); // 현재 진행 중인 작업 정보
    public ProcessInfo ProcessInfo { get; set; } = new ProcessInfo(); // 현재 인식된 공정 정보
    public NextProcessInfo NextProcessInfo { get; set; } = new NextProcessInfo(); // 다음에 진행할 공정 정보
    public WorkFlowResult Result { get; set; } // 결과 코드
    public int STATUS { get; set; }        // 다음 공정의 상태 (0: Input, 1: Output)

    public void ByteToData(byte[] bytes, ref int index)
    {
        Result = (WorkFlowResult)TCPConverter.ToInt(bytes, ref index);

        WorkInfo.ByteToData(bytes, ref index);

        ProcessInfo.ByteToData(bytes, ref index);

        NextProcessInfo.ByteToData(bytes, ref index);

        STATUS = TCPConverter.ToInt(bytes, ref index);
    }

    public void DataToByte(byte[] bytes, ref int index)
    {
        TCPConverter.SetBytes(bytes, (int)Result, ref index);

        WorkInfo.DataToByte(bytes, ref index);  // 반드시 데이터를 보냄
        ProcessInfo.DataToByte(bytes, ref index);  // ProcessInfo도 항상 데이터를 보냄
        NextProcessInfo.DataToByte(bytes, ref index);  // NextProcessInfo도 항상 보냄

        TCPConverter.SetBytes(bytes, STATUS, ref index);
    }
}

public class FuncExecuteWorkFlow : FuncInfo
{
    public ExecuteWorkFlowRequest RequestData { get; set; } = new ExecuteWorkFlowRequest();
    public ExecuteWorkFlowResponse ResponseData { get; private set; } = new ExecuteWorkFlowResponse();

    protected override int GetFuncID()
    {
        return (int)FuncID.EXECUTE_WORK_FLOW;
    }

    public override bool DBQuery()
    {
        try
        {
            if (RequestData.PID < 0)
            {
                if (CheckInProcessInfoByQRCode(RequestData.QRCode))
                {
                    return true;
                }
                // QR 코드로 공정 정보를 찾지 못했을 때, 작업 정보 확인
                WorkInfo workInfo = GetWorkInfoQR(RequestData.QRCode);
                if (workInfo != null)
                {
                    ResponseData.WorkInfo = workInfo;
                    
                    // 해당 작업에 대한 진행 중인 최신 공정 확인
                    string latestProcessQuery = $@"
                    WITH LatestProcess AS (
                        SELECT DISTINCT ON (serial_no) *
                        FROM work_flow
                        WHERE serial_no = '{workInfo.SERIAL_NO}'
                        ORDER BY serial_no, reg_dt DESC
                    )
                    SELECT * FROM LatestProcess";
                    
                    DataTable latestProcessResult = Query(latestProcessQuery);
                    
                    if (latestProcessResult != null && latestProcessResult.Rows.Count > 0)
                    {
                        // 진행 중인 공정이 있는 경우
                        DataRow row = latestProcessResult.Rows[0];
                        int currentProcId = ToInt(row, "proc_id");
                        int currentStatus = ToInt(row, "status");
                        int currentIndex = Array.IndexOf(workInfo.PROCESS_LIST, currentProcId);
                        
                        // 현재 상태가 1(Output)이고 다음 공정이 있으면
                        if (currentStatus == 1 && currentIndex < workInfo.PROCESS_LIST.Length - 1)
                        {
                            int nextProcId = workInfo.PROCESS_LIST[currentIndex + 1];
                            ResponseData.NextProcessInfo = new NextProcessInfo
                            {
                                PID = nextProcId,
                                STATUS = 0, // 다음 공정의 Input
                                ProcessInfo = GetProcessInfo(nextProcId)
                            };
                        }
                        else if (currentStatus == 0) // 현재 공정의 Input만 인식된 상태
                        {
                            ResponseData.NextProcessInfo = new NextProcessInfo
                            {
                                PID = currentProcId,
                                STATUS = 1, // 현재 공정의 Output
                                ProcessInfo = GetProcessInfo(currentProcId)
                            };
                        }
                        else if (currentStatus == 1 && currentIndex == workInfo.PROCESS_LIST.Length - 1)
                        {
                            // 모든 공정이 완료된 상태
                            ResponseData.NextProcessInfo = new NextProcessInfo
                            {
                                PID = -1,
                                STATUS = -1
                            };
                            ResponseData.Result = WorkFlowResult.SuccessCompleted;
                        }
                    }
                    else
                    {
                        // 진행 중인 공정이 없는 경우, 첫 번째 공정을 시작
                        int firstProcId = workInfo.PROCESS_LIST[0];
                        ResponseData.NextProcessInfo = new NextProcessInfo
                        {
                            PID = firstProcId,
                            STATUS = 0, // 첫 번째 공정의 Input
                            ProcessInfo = GetProcessInfo(firstProcId)
                        };
                    }
                    
                    return true;
                }
                
                // 공정 정보도 없고 작업 정보도 없는 경우
                ResponseData.Result = WorkFlowResult.ErrorNoProcessInfo;
            }
            else
            {
                // 1. 진행 중인 작업 제품의 QR 코드 확인
                var work = GetWorkInfoQR(RequestData.QRCode);
                if(work != null)
                {
                    var process = GetProcessInfo(RequestData.PID);
                    if (CheckProgressTracking(work.SERIAL_NO, RequestData.STATUS == 0 ? process.INPUT_QR : process.OUTPUT_QR))
                    {
                        return true; // 시리얼 넘버로 진행 상황을 찾았으면 처리 완료
                    }
                }
                ResponseData.Result = WorkFlowResult.ErrorNoWorkInfo;
            }

            /*
            // 1. 진행 중인 작업 제품의 QR 코드 확인
            if (CheckInProgressWorkInfoByQRCode(RequestData.QRCode))
            {
                return true; // 진행 중인 작업 제품이 존재하면 처리 완료
            }

            // 현재 진행중인 작업이 있을 때 (작업 정보를 인식한 상태)
            if (!string.IsNullOrEmpty(RequestData.SerialNumber))
            {
                // 2. 시리얼 넘버로 진행 중인 공정 확인
                if ( CheckProgressTracking(RequestData.SerialNumber, RequestData.QRCode))
                {
                    return true; // 시리얼 넘버로 진행 상황을 찾았으면 처리 완료
                }
            }
            */
             
            // 4. 해당하지 않는 QR 코드 처리 // 아예 잘못된 큐알 코드
            return true;
        }
        catch (Exception ex)
        {
            Common.DEBUG("DBQuery 실행 중 오류 발생: " + ex.Message);
            Debug.LogError(ex.StackTrace);
            if (ex.InnerException != null)
            {
                Debug.LogError(ex.InnerException.StackTrace);
            }
            return false;
        }
    }
    private bool CheckInProcessInfoByQRCode(string qrCode)
    {
        // SQL 쿼리: QR 코드가 input_qr 또는 output_qr에 해당하는지 확인
        string query = $@"
        SELECT *
        FROM process_info
        WHERE input_qr = '{qrCode}' OR output_qr = '{qrCode}'
        LIMIT 1";

        // 데이터베이스 쿼리 실행
        DataTable result = Query(query);

        if (result != null && result.Rows.Count > 0)
        {
            // QR 코드가 프로세스 인포의 input_qr 또는 output_qr에 존재하는 경우
            DataRow row = result.Rows[0];
            ResponseData.ProcessInfo = CreateProcessInfoFromRow(row);
            ResponseData.STATUS = ResponseData.ProcessInfo.INPUT_QR == qrCode ? 0 : 1;

            ResponseData.Result = WorkFlowResult.SuccessRecognizeProcess;

            // QR 코드가 프로세스 인포에 존재하면 true 반환
            return true;
        }

        // QR 코드가 존재하지 않으면 false 반환
        return false;
    }


    // 1. 진행 중인 작업 제품의 QR 코드가 있는지 확인
    private bool CheckInProgressWorkInfoByQRCode(string qrCode)
    {
        string query = $@"
        WITH LatestWorkInfo AS (
            SELECT *
            FROM work_info
            WHERE qr = '{qrCode}'
            ORDER BY reg_dt DESC
            LIMIT 1
        ),
        LatestProcessTracking AS (
            SELECT pt.*
            FROM work_flow pt
            INNER JOIN LatestWorkInfo lw ON pt.serial_no = lw.serial_no
            WHERE pt.status = 0 -- 진행 중인 공정 상태
            ORDER BY pt.reg_dt DESC
            LIMIT 1
        )
        SELECT lw.*, pt.proc_id, pt.status AS tracking_status, pt.reg_dt AS tracking_reg_dt
        FROM LatestWorkInfo lw
        LEFT JOIN LatestProcessTracking pt ON lw.serial_no = pt.serial_no
        ORDER BY lw.reg_dt DESC
        LIMIT 1";

        DataTable result = Query(query);

        if (result != null && result.Rows.Count > 0)
        {
            DataRow row = result.Rows[0];
            ResponseData.WorkInfo = CreateWorkInfoFromRow(row);

            // work_flow에 아직 정보가 없을 경우 (공정을 시작하지 않은 경우)
            if (IsNullOrEmpty(row["proc_id"]))
            {
                // 공정이 시작되지 않았으므로 첫 번째 공정 정보를 NextProcessInfo로 설정
                int firstProcessId = ResponseData.WorkInfo.PROCESS_LIST[0];
                ResponseData.NextProcessInfo = new NextProcessInfo
                {
                    PID = firstProcessId,
                    STATUS = 0, // 첫 번째 공정의 Input 상태
                    ProcessInfo = GetProcessInfo(firstProcessId)
                };

                //ResponseData.Result = ProcessTrackingResult.SuccessRecognizeWork;
                return true; // 작업이 진행 중인 상태로 반환
            }

            // 공정이 진행 중인 경우 // 제품 인식만 알려줘도 될 것 같음
            /*
            ResponseData.ProcessInfo = CreateProcessInfoFromRow(row);

            // 다음 공정 확인 및 NextProcessInfo 설정
            int currentProcessIndex = Array.IndexOf(ResponseData.WorkInfo.PROCESS_LIST, ResponseData.ProcessInfo.PID);
            if (currentProcessIndex >= 0 && currentProcessIndex < ResponseData.WorkInfo.PROCESS_LIST.Length - 1)
            {
                int nextProcessId = ResponseData.WorkInfo.PROCESS_LIST[currentProcessIndex + 1];
                ResponseData.NextProcessInfo = new NextProcessInfo
                {
                    PID = nextProcessId,
                    STATUS = 0, // 기본적으로 다음 공정의 Input 상태
                    ProcessInfo = GetProcessInfo(nextProcessId)
                };
            }
            else
            {
                ResponseData.NextProcessInfo = new NextProcessInfo { PID = -1, STATUS = -1 }; // 더 이상 진행할 공정 없음
            }
            */

            //ResponseData.Result = ProcessTrackingResult.SuccessRecognizeWork;
            return true;
        }

        return false;
    }


    // 2. 시리얼 넘버로 진행 상황을 확인
    private bool CheckProgressTracking(string serialNumber, string qr)
    {
        // 로그 추가: 함수 호출 정보
        Common.DEBUG($"===== CheckProgressTracking 호출 =====");
        Common.DEBUG($"serialNumber: {serialNumber}, qr: {qr}");
        
        // 시리얼 넘버로 작업 정보를 가져옴
        ResponseData.WorkInfo = GetWorkInfo(serialNumber);
        
        // 로그 추가: WorkInfo 정보
        if (ResponseData.WorkInfo != null)
        {
            Common.DEBUG($"WorkInfo 불러옴: {ResponseData.WorkInfo.SERIAL_NO}");
            if (ResponseData.WorkInfo.PROCESS_LIST != null)
            {
                Common.DEBUG($"PROCESS_LIST 길이: {ResponseData.WorkInfo.PROCESS_LIST.Length}");
                Common.DEBUG($"PROCESS_LIST 내용: {string.Join(", ", ResponseData.WorkInfo.PROCESS_LIST)}");
            }
            else
            {
                Common.DEBUG("PROCESS_LIST가 null입니다");
            }
        }
        else
        {
            Common.DEBUG($"WorkInfo가 null입니다: {serialNumber}");
        }
        
        // 작업 정보가 없는 경우 처리
        if (ResponseData.WorkInfo == null || ResponseData.WorkInfo.PROCESS_LIST == null || ResponseData.WorkInfo.PROCESS_LIST.Length == 0)
        {
            ResponseData.Result = WorkFlowResult.ErrorNoWorkInfo;
            Common.DEBUG($"작업 정보가 없거나 공정 목록이 비어있습니다: {serialNumber}");
            return false;
        }
        
        // 만약 어떤 경우에도 해당하지 않는다면 잘못된 QR 코드 처리
        ResponseData.Result = WorkFlowResult.ErrorInvalidQR;

        string query = $@"
        WITH LatestTracking AS (
            SELECT pt.*
            FROM work_flow pt
            INNER JOIN work_info wi ON pt.serial_no = wi.serial_no
            WHERE pt.serial_no = '{serialNumber}' AND pt.reg_dt > wi.reg_dt  -- work_info 생성 후의 트래킹 정보만
            ORDER BY pt.reg_dt DESC
            LIMIT 1
        )
        SELECT lt.*, pi.proc_nm, pi.proc_loc, pi.input_qr, pi.output_qr
        FROM LatestTracking lt
        LEFT JOIN process_info pi ON lt.proc_id = pi.pid";

        Common.DEBUG($"쿼리 실행: {query}");
        DataTable result = Query(query);
        Common.DEBUG($"쿼리 결과: {(result != null ? result.Rows.Count : 0)}행");

        // Case 1: work_flow에 정보가 없는 경우
        if (result == null || result.Rows.Count == 0)
        {
            Common.DEBUG("work_flow에 정보가 없음, 첫 번째 공정으로 설정");
            // work_flow 정보가 없는 경우, 첫 번째 공정의 정보를 NextProcessInfo로 설정
            int firstProcessId = ResponseData.WorkInfo.PROCESS_LIST[0];  // proc_list의 첫 번째 PID
            Common.DEBUG($"첫 번째 공정 ID: {firstProcessId}");
            ProcessInfo nextProcess = GetProcessInfo(firstProcessId);
            
            if (nextProcess == null)
            {
                Common.DEBUG($"첫 번째 공정 정보를 찾을 수 없습니다. PID: {firstProcessId}");
                return false;
            }

            Common.DEBUG($"첫 번째 공정 정보: {nextProcess.PROC_NM}, InputQR: {nextProcess.INPUT_QR}, OutputQR: {nextProcess.OUTPUT_QR}");
            ResponseData.NextProcessInfo = new NextProcessInfo
            {
                PID = firstProcessId,
                STATUS = 0,  // 첫 번째 공정의 Input 상태
                ProcessInfo = nextProcess
            };

            if (nextProcess.INPUT_QR == qr)
            {
                Common.DEBUG($"첫 번째 공정의 Input QR과 일치: {qr}");
                ResponseData.Result = WorkFlowResult.SuccessRecognizeWork;
                ResponseData.NextProcessInfo.STATUS = 1;

                // 먼저 work_log에 기록
                int logId = InsertProgressLog(serialNumber, firstProcessId);
                Common.DEBUG($"InsertProgressLog 결과 logId: {logId}");
                if (logId > 0)
                {
                    // work_flow에 log_id와 함께 기록
                    InsertProgressTracking(serialNumber, firstProcessId, 0, logId);
                }
            }
            else
            {
                Common.DEBUG($"QR 코드 불일치: 입력={qr}, 기대값={nextProcess.INPUT_QR}");
            }

            return true;
        }

        // Case 2: work_flow에 정보가 있는 경우
        DataRow row = result.Rows[0];
        int currentProcId = ToInt(row, "proc_id");
        int currentStatus = ToInt(row, "status");
        Common.DEBUG($"현재 공정 ID: {currentProcId}, 상태: {currentStatus}");

        ResponseData.ProcessInfo = GetProcessInfo(currentProcId);
        if (ResponseData.ProcessInfo == null)
        {
            Common.DEBUG($"현재 공정 정보를 찾을 수 없습니다. PID: {currentProcId}");
            return false;
        }
        Common.DEBUG($"현재 공정 정보: {ResponseData.ProcessInfo.PROC_NM}");

        // 현재 PID의 위치를 proc_list에서 찾음
        int currentProcessIndex = Array.IndexOf(ResponseData.WorkInfo.PROCESS_LIST, currentProcId);
        Common.DEBUG($"현재 공정 인덱스: {currentProcessIndex}, PROCESS_LIST 길이: {ResponseData.WorkInfo.PROCESS_LIST.Length}");
        
        if (currentProcessIndex == -1) 
        {
            // 공정 목록에 없는 PID가 지정된 경우
            Common.DEBUG($"현재 공정 ID({currentProcId})가 작업의 공정 목록에 없습니다: {string.Join(", ", ResponseData.WorkInfo.PROCESS_LIST)}");
            return false;
        }

        // Case 2.1: 현재 status가 0인 경우, 즉 Input이 인식된 상태에서 Output QR이 인식되어야 하는 상황
        if (currentStatus == 0)
        {
            Common.DEBUG("Case 2.1: Input이 인식된 상태, Output QR 기다림");
            // 다음 공정이 있는지 확인
            ResponseData.NextProcessInfo = new NextProcessInfo
            {
                PID = currentProcId,
                STATUS = 1,  // Output 상태를 기다림
                ProcessInfo = ResponseData.ProcessInfo
            };

            // 만약 현재 Input QR을 다시 스캔한 경우 오류 처리
            if (ResponseData.ProcessInfo.INPUT_QR == qr)
            {
                Common.DEBUG($"이미 인식된 Input QR을 다시 스캔함: {qr}");
                ResponseData.Result = WorkFlowResult.ErrorInvalidQR;
                return true;
            }

            Common.DEBUG($"Output QR 기대값: {ResponseData.NextProcessInfo.ProcessInfo.OUTPUT_QR}, 입력된 QR: {qr}");
            if (ResponseData.NextProcessInfo.ProcessInfo.OUTPUT_QR == qr)
            {
                Common.DEBUG($"Output QR 인식 성공");
                ResponseData.Result = WorkFlowResult.SuccessRecognizeWork;

                // 현재 work_flow 레코드의 log_id 조회
                string logQuery = $@"
                SELECT log_id 
                FROM work_flow 
                WHERE serial_no = '{serialNumber}' 
                AND proc_id = {currentProcId} 
                AND status = 0 
                ORDER BY reg_dt DESC 
                LIMIT 1";

                DataTable logResult = Query(logQuery);
                if (logResult != null && logResult.Rows.Count > 0)
                {
                    int logId = ToInt(logResult.Rows[0], "log_id");
                    Common.DEBUG($"찾은 log_id: {logId}");
                    // 기존 log_id를 사용하여 output 상태 기록
                    InsertProgressTracking(serialNumber, currentProcId, 1, logId);
                    
                    // work_log의 output_time 업데이트
                    string updateQuery = $@"
                    UPDATE work_log 
                    SET output_qr = '{qr}', output_time = CURRENT_TIMESTAMP 
                    WHERE log_id = {logId}";
                    
                    Query(updateQuery);
                }

                if (currentProcessIndex == ResponseData.WorkInfo.PROCESS_LIST.Length - 1)
                {
                    Common.DEBUG("모든 공정이 완료됨");
                    // 모든 공정이 완료된 상태, 다음 공정이 없음
                    ResponseData.NextProcessInfo = new NextProcessInfo { PID = -1, STATUS = -1 };
                    ResponseData.Result = WorkFlowResult.SuccessCompleted;
                }
            }
            else
            {
                Common.DEBUG($"Output QR 불일치: 입력={qr}, 기대값={ResponseData.NextProcessInfo.ProcessInfo.OUTPUT_QR}");
            }
            return true;
        }
        // Case 2.2: 현재 status가 1인 경우, 다음 공정으로 이동
        else if (currentStatus == 1)
        {
            Common.DEBUG("Case 2.2: Output이 인식된 상태, 다음 공정으로 이동");
            // 다음 공정이 있는지 확인
            if (currentProcessIndex < ResponseData.WorkInfo.PROCESS_LIST.Length - 1)
            {
                Common.DEBUG($"다음 공정이 있음. 현재 인덱스: {currentProcessIndex}, 총 공정 수: {ResponseData.WorkInfo.PROCESS_LIST.Length}");
                int nextProcessId = ResponseData.WorkInfo.PROCESS_LIST[currentProcessIndex + 1];
                Common.DEBUG($"다음 공정 ID: {nextProcessId}");
                ProcessInfo nextProcessInfo = GetProcessInfo(nextProcessId);
                
                if (nextProcessInfo == null)
                {
                    Common.DEBUG($"다음 공정 정보를 찾을 수 없습니다. PID: {nextProcessId}");
                    return false;
                }

                ResponseData.NextProcessInfo = new NextProcessInfo
                {
                    PID = nextProcessId,
                    STATUS = 0,  // 다음 공정의 Input 상태를 기다림
                    ProcessInfo = nextProcessInfo
                };

                Common.DEBUG($"다음 공정: {nextProcessInfo.PROC_NM}, InputQR: {nextProcessInfo.INPUT_QR}, 입력된 QR: {qr}");

                if (ResponseData.NextProcessInfo.ProcessInfo.INPUT_QR == qr)
                {
                    Common.DEBUG("다음 공정의 Input QR 인식됨");
                    ResponseData.Result = WorkFlowResult.SuccessRecognizeWork;

                    // 먼저 work_log에 기록
                    int logId = InsertProgressLog(serialNumber, nextProcessId);
                    Common.DEBUG($"InsertProgressLog 결과 logId: {logId}");
                    if (logId > 0)
                    {
                        // work_flow에 log_id와 함께 기록
                        InsertProgressTracking(serialNumber, nextProcessId, 0, logId);
                    }
                }
                else
                {
                    Common.DEBUG($"Input QR 불일치: 입력={qr}, 기대값={nextProcessInfo.INPUT_QR}");
                }
            }
            else
            {
                Common.DEBUG("다음 공정이 없음(모든 공정 완료)");
                // 모든 공정이 완료된 상태
                ResponseData.NextProcessInfo = new NextProcessInfo { PID = -1, STATUS = -1 };
                ResponseData.Result = WorkFlowResult.SuccessCompleted;
            }
            return true;
        }
        else
        {
            Common.DEBUG($"알 수 없는 상태값: {currentStatus}");
        }

        Common.DEBUG("===== CheckProgressTracking 종료 =====");
        return false;
    }

    public bool InsertProgressTracking(string serialNo, int procId, int status, int logId)
    {
        try
        {
            string query = $@"
            INSERT INTO work_flow (serial_no, proc_id, status, log_id, reg_dt)
            VALUES ('{serialNo}', {procId}, {status}, {logId}, CURRENT_TIMESTAMP)";

            Insert(query);
            return true;
        }
        catch (Exception ex)
        {
            Common.DEBUG("InsertProgressTracking 함수 실행 중 오류 발생: " + ex.Message);
            return false;
        }
    }

    private int InsertProgressLog(string serialNo, int procId)
    {
        try
        {
            // 작업 정보와 공정 정보 조회
            ProcessInfo processInfo = GetProcessInfo(procId);
            WorkInfo workInfo = GetWorkInfo(serialNo);

            // workInfo나 processInfo가 null인 경우 체크
            if (workInfo == null || processInfo == null)
            {
                Common.DEBUG("작업 정보 또는 공정 정보를 찾을 수 없습니다.");
                return -1;
            }

            string parametersJson = JsonUtility.ToJson(new ProcessParameters 
                    { 
                        parameters = processInfo.PARAMETERS?.Select(p => new ProcessParameter 
                        { 
                            key = p.Key, 
                            value = p.Value
                        }).ToList() ?? new List<ProcessParameter>() 
                    });
            Common.DEBUG($"Parameters being inserted: {parametersJson}"); // 디버그 로그 추가

            // proc_list를 PostgreSQL 배열 형식으로 변환
            string procListArray = $"{{{string.Join(",", workInfo.PROCESS_LIST)}}}";

            // progress_log에 기록하고 생성된 log_id를 반환받음
            string logQuery = $@"
            INSERT INTO work_log (
                serial_no, comp_nm, mgr_nm, work_qr, proc_list,
                proc_id, proc_nm, proc_loc, 
                input_qr, parameters,
                input_time
            ) VALUES (
                '{serialNo}', 
                '{workInfo.COMP_NM}', 
                '{workInfo.MNG_NM}', 
                '{workInfo.QR_CODE}', 
                '{procListArray}',
                {procId}, 
                '{processInfo.PROC_NM}', 
                '{processInfo.PROC_LOC}',
                '{processInfo.INPUT_QR}', 
                '{parametersJson}'::jsonb,
                CURRENT_TIMESTAMP
            ) RETURNING log_id";

            DataTable result = Query(logQuery);
            if (result != null && result.Rows.Count > 0)
            {
                return ToInt(result.Rows[0], "log_id");
            }
            return -1;
        }
        catch (Exception ex)
        {
            Common.DEBUG("InsertProgressLog 함수 실행 중 오류 발생: " + ex.Message);
            return -1;
        }
    }

    // 작업 정보를 생성하는 메서드
    private WorkInfo GetWorkInfo(string serialNumber)
    {
        string workQuery = $@"
            SELECT *
            FROM work_info
            WHERE serial_no = '{serialNumber}'";
        DataTable workResult = Query(workQuery);

        if (workResult != null && workResult.Rows.Count > 0)
        {
            return CreateWorkInfoFromRow(workResult.Rows[0]);
        }
        return null;
    }
    private WorkInfo GetWorkInfoQR(string qr)
    {
        // 로그 추가
        Common.DEBUG($"GetWorkInfoQR 호출: QR={qr}");
        
        // QR로 모든 work_info 조회
        string workQuery = $@"
            WITH WorkInfoList AS (
                SELECT wi.*
                FROM work_info wi
                WHERE wi.qr = '{qr}'
            ),
            LatestWorkFlow AS (
                SELECT DISTINCT ON (wf.serial_no) 
                    wf.serial_no, 
                    wf.proc_id,
                    wf.status,
                    CASE 
                        WHEN wf.proc_id IS NULL THEN 0  -- 아직 시작 안됨
                        WHEN (
                            SELECT COUNT(*) 
                            FROM work_flow 
                            WHERE serial_no = wf.serial_no
                        ) >= (
                            SELECT array_length(proc_list, 1) * 2  -- 각 공정마다 input/output 상태가 있으므로 *2
                            FROM work_info 
                            WHERE serial_no = wf.serial_no
                        ) THEN 2  -- 모든 공정 완료
                        ELSE 1  -- 진행 중
                    END as completion_status
                FROM work_flow wf
                RIGHT JOIN WorkInfoList wil ON wf.serial_no = wil.serial_no
                ORDER BY wf.serial_no, wf.reg_dt DESC
            )
            SELECT wi.*, COALESCE(lwf.completion_status, 0) as completion_status
            FROM WorkInfoList wi
            LEFT JOIN LatestWorkFlow lwf ON wi.serial_no = lwf.serial_no
            ORDER BY 
                CASE 
                    WHEN COALESCE(lwf.completion_status, 0) = 1 THEN 0  -- 진행 중인 작업 우선
                    WHEN COALESCE(lwf.completion_status, 0) = 0 THEN 1  -- 다음은 시작 안된 작업
                    ELSE 2  -- 마지막으로 완료된 작업
                END,
                wi.reg_dt DESC  -- 같은 상태면 최근 등록된 작업 우선
            LIMIT 1";
            
        Common.DEBUG($"실행 쿼리: {workQuery}");
        DataTable workResult = Query(workQuery);
        Common.DEBUG($"쿼리 결과 행 수: {(workResult != null ? workResult.Rows.Count : 0)}");

        if (workResult != null && workResult.Rows.Count > 0)
        {
            WorkInfo info = CreateWorkInfoFromRow(workResult.Rows[0]);
            int completionStatus = ToInt(workResult.Rows[0], "completion_status");
            Common.DEBUG($"선택된 작업: {info.SERIAL_NO}, 완료 상태: {completionStatus}");
            return info;
        }
        
        Common.DEBUG("QR 코드로 작업 정보를 찾을 수 없음");
        return null;
    }

    // 공정 정보를 생성하는 메서드
    private ProcessInfo GetProcessInfo(int processId)
    {
        string processQuery = $@"
            SELECT *
            FROM process_info
            WHERE pid = {processId}";
        DataTable processResult = Query(processQuery);

        if (processResult != null && processResult.Rows.Count > 0)
        {
            return CreateProcessInfoFromRow(processResult.Rows[0]);
        }
        return null;
    }

    // work_info에서 DataRow를 WorkInfo로 변환
    private WorkInfo CreateWorkInfoFromRow(DataRow row)
    {
        return new WorkInfo
        {
            SERIAL_NO = ToString(row, "serial_no"),
            QR_CODE = ToString(row, "qr"),
            COMP_NM = ToString(row, "comp_nm"),
            MNG_NM = ToString(row, "mgr_nm"),
            PROCESS_LIST = ToIntArray(row, "proc_list"),
            Reg_DT = (DateTime)row["reg_dt"]
        };
    }

    // process_info에서 DataRow를 ProcessInfo로 변환
    private ProcessInfo CreateProcessInfoFromRow(DataRow row)
    {
        ProcessInfo processInfo = new ProcessInfo
        {
            PID = ToInt(row, "pid"),
            PROC_NM = ToString(row, "proc_nm"),
            PROC_LOC = ToString(row, "proc_loc"),
            INPUT_QR = ToString(row, "input_qr"),
            OUTPUT_QR = ToString(row, "output_qr"),
            REG_DT = (DateTime)row["reg_dt"]
        };

        // parameters 처리 추가
        string jsonStr = ToString(row, "parameters");
        Common.DEBUG($"DB에서 읽은 parameters JSON: {jsonStr}");

        if (!string.IsNullOrEmpty(jsonStr))
        {
            try
            {
                var paramList = JsonUtility.FromJson<ProcessParameters>(jsonStr);
                Common.DEBUG($"파싱된 parameters 개수: {paramList?.parameters?.Count ?? 0}");
                
                processInfo.PARAMETERS.Clear();
                if (paramList != null && paramList.parameters != null)
                {
                    foreach (var param in paramList.parameters)
                    {
                        processInfo.PARAMETERS[param.key] = param.value;
                        Common.DEBUG($"파라미터 추가: {param.key} = {param.value}");
                    }
                }
            }
            catch (Exception ex)
            {
                Common.DEBUG($"Parameters 파싱 중 오류: {ex.Message}");
                Common.DEBUG($"오류가 발생한 JSON 문자열: {jsonStr}");
            }
        }
        else
        {
            Common.DEBUG("parameters 값이 비어있음");
        }

        return processInfo;
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

        // 결과에 대한 임시 로그 출력
        string resultMessage = "";
        switch (ResponseData.Result)
        {
            case WorkFlowResult.SuccessRecognizeWork:
                resultMessage = $"작업 인식 성공: {ResponseData.WorkInfo.SERIAL_NO}, PID: {ResponseData.NextProcessInfo.PID}, STATUS: {ResponseData.NextProcessInfo.STATUS}";
                break;
            case WorkFlowResult.SuccessRecognizeProcess:
                resultMessage = $"공정 인식 성공: {ResponseData.ProcessInfo.PROC_NM}, PID: {ResponseData.ProcessInfo.PID}, STATUS: {ResponseData.STATUS}";
                break;
            case WorkFlowResult.SuccessCompleted:
                resultMessage = $"모든 공정 완료: {ResponseData.WorkInfo.SERIAL_NO}";
                break;
            case WorkFlowResult.ErrorInvalidQR:
                resultMessage = $"잘못된 QR 코드: {RequestData.QRCode}";
                break;
            case WorkFlowResult.ErrorNoProcessInfo:
                resultMessage = $"공정 정보 없음: {RequestData.QRCode}";
                break;
            case WorkFlowResult.ErrorNoWorkInfo:
                resultMessage = $"작업 정보 없음: {RequestData.QRCode}";
                break;
            default:
                resultMessage = $"알 수 없는 결과: {ResponseData.Result}";
                break;
        }
        
        //Common.DEBUG($"[워크플로우 결과] {resultMessage}");
        //Debug.Log($"[WorkFlow Result] {resultMessage}");

        // Response 데이터 처리
        ResponseData.DataToByte(bytes, ref index);

        return FitBytes(bytes, index);
    }
}