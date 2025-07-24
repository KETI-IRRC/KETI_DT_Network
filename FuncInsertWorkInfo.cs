using System;
using System.Data;
using System.Collections.Generic;
using UnityEngine;

// 결과를 Enum으로 정의
public enum WorkInsertResult
{
    Error = 0,
    Success,                     // 작업 등록 성공
    ErrorActiveProcessTracking,   // 프로세스가 종료되지 않은 상태
    ErrorInsertFailed             // 작업 등록 실패
}

// 보내지는 데이터를 처리하는 클래스
public class WorkInsertRequest
{
    public string QRCode { get; set; }
    public string CompanyName { get; set; }
    public string ManagerName { get; set; }
    public List<int> ProcessList { get; set; }

    public void ByteToData(byte[] bytes, ref int index)
    {
        QRCode = TCPConverter.ToString(bytes, ref index);
        CompanyName = TCPConverter.ToString(bytes, ref index);
        ManagerName = TCPConverter.ToString(bytes, ref index);

        int processCount = TCPConverter.ToInt(bytes, ref index);
        ProcessList = new List<int>();
        for (int i = 0; i < processCount; i++)
        {
            ProcessList.Add(TCPConverter.ToInt(bytes, ref index));
        }
    }

    public void DataToByte(byte[] bytes, ref int index)
    {
        TCPConverter.SetBytes(bytes, QRCode, ref index);
        TCPConverter.SetBytes(bytes, CompanyName, ref index);
        TCPConverter.SetBytes(bytes, ManagerName, ref index);

        int processCount = ProcessList.Count;
        TCPConverter.SetBytes(bytes, processCount, ref index);
        for (int i = 0; i < processCount; i++)
        {
            TCPConverter.SetBytes(bytes, ProcessList[i], ref index);
        }
    }
}

// 받는 데이터를 처리하는 클래스
public class WorkInsertResponse
{
    public WorkInsertResult Result { get; set; } // 작업 등록 결과
    public string SerialNumber { get; set; }
    public DateTime RegistrationTime { get; set; }

    public void ByteToData(byte[] bytes, ref int index)
    {
        Result = (WorkInsertResult)TCPConverter.ToInt(bytes, ref index);
        SerialNumber = TCPConverter.ToString(bytes, ref index);
        RegistrationTime = TCPConverter.ToDateTime(bytes, ref index);
    }

    public void DataToByte(byte[] bytes, ref int index)
    {
        TCPConverter.SetBytes(bytes, (int)Result, ref index);
        TCPConverter.SetBytes(bytes, SerialNumber, ref index);
        TCPConverter.SetBytes(bytes, RegistrationTime, ref index);
    }
}

// 작업 등록을 처리하는 메인 클래스
public class FuncInsertWorkInfo : FuncInfo
{
    public WorkInsertRequest RequestData { get; set; } = new WorkInsertRequest();
    public WorkInsertResponse ResponseData { get; private set; } = new WorkInsertResponse();

    protected override int GetFuncID()
    {
        return (int)FuncID.WORK_INFO_INSERT; // FuncID에서 작업 등록에 대한 ID
    }
    public override bool DBQuery()
    {
        try
        {
            // 1. QR 코드가 work_flow에서 해당 작업에 대한 모든 공정이 완료되었는지 확인
            string query = $@"
            SELECT wi.serial_no, array_to_string(wi.proc_list, ',') as proc_list, 
                   MAX(pt.proc_id) AS last_proc_id, MAX(pt.status) AS last_status
            FROM work_info wi
            LEFT JOIN work_flow pt ON wi.serial_no = pt.serial_no
            WHERE wi.qr = '{RequestData.QRCode}'
            GROUP BY wi.serial_no, wi.proc_list";

            DataTable result = Query(query);
            if (result != null && result.Rows.Count > 0)
            {
                // 기존 작업의 진행 상태를 확인
                DataRow row = result.Rows[0];

                // 배열을 문자열로 받으므로 다시 배열로 변환
                int[] processList = Array.ConvertAll(row["proc_list"].ToString().Split(','), int.Parse);

                int lastProcId = ToInt(row, "last_proc_id");
                int lastStatus = ToInt(row, "last_status");

                // 마지막 공정이 완료되지 않은 경우 (마지막 공정의 상태가 완료(1)이 아니거나, 아직 남은 공정이 있을 때)
                if (lastStatus != 1 || Array.IndexOf(processList, lastProcId) != processList.Length - 1)
                {
                    ResponseData.Result = WorkInsertResult.ErrorActiveProcessTracking;
                    Common.DEBUG("진행 중인 공정이 있어 작업을 등록할 수 없습니다.");
                    return true;
                }
            }

            // 2. 모든 공정이 완료된 상태이면 새로운 작업을 등록
            string insertQuery = $@"
            INSERT INTO work_info (qr, comp_nm, mgr_nm, proc_list)
            VALUES ('{RequestData.QRCode}', '{RequestData.CompanyName}', '{RequestData.ManagerName}', ARRAY[{string.Join(",", RequestData.ProcessList)}]::integer[])
            RETURNING serial_no, reg_dt;";

            DataTable insertResult = Query(insertQuery);
            if (insertResult == null || insertResult.Rows.Count == 0)
            {
                // 작업 등록 실패
                ResponseData.Result = WorkInsertResult.ErrorInsertFailed;
                Common.DEBUG("작업 등록 실패");
                return true;
            }

            // 3. 작업 등록 성공 시 반환할 데이터 설정
            ResponseData.SerialNumber = insertResult.Rows[0]["serial_no"].ToString();
            ResponseData.RegistrationTime = (DateTime)insertResult.Rows[0]["reg_dt"];
            ResponseData.Result = WorkInsertResult.Success;

            Common.DEBUG("작업 등록 성공");
            return true;
        }
        catch (Exception ex)
        {
            Common.DEBUG($"DBQuery 실행 중 오류 발생: {ex.Message}");
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
