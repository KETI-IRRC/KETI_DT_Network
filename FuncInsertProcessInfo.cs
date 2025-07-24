using System;
using System.Data;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

// 보내지는 데이터를 처리하는 클래스
public class ProcessInsertRequest
{
    public int PID { get; set; } = -1;            // -1이면 Insert, 그 외에는 Update
    public string ProcessName { get; set; }
    public string ProcessLocation { get; set; }
    public string InputQR { get; set; }
    public string OutputQR { get; set; }
    public Dictionary<string, string> Parameters { get; set; }

    public void ByteToData(byte[] bytes, ref int index)
    {
        PID = TCPConverter.ToInt(bytes, ref index);
        ProcessName = TCPConverter.ToString(bytes, ref index);
        ProcessLocation = TCPConverter.ToString(bytes, ref index);
        InputQR = TCPConverter.ToString(bytes, ref index);
        OutputQR = TCPConverter.ToString(bytes, ref index);
        Parameters = TCPConverter.ToJsonDictionary(bytes, ref index);
    }

    public void DataToByte(byte[] bytes, ref int index)
    {
        TCPConverter.SetBytes(bytes, PID, ref index);
        TCPConverter.SetBytes(bytes, ProcessName, ref index);
        TCPConverter.SetBytes(bytes, ProcessLocation, ref index);
        TCPConverter.SetBytes(bytes, InputQR, ref index);
        TCPConverter.SetBytes(bytes, OutputQR, ref index);
        TCPConverter.SetBytes(bytes, Parameters, ref index);
    }
}

// 받는 데이터를 처리하는 클래스
public class ProcessInsertResponse
{
    public enum ProcessInsertResult
    {
        Success = 0,            // 성공
        DuplicateInputQR = 1,   // Input QR 정보가 중복된 경우
        DuplicateOutputQR = 2,  // Output QR 정보가 중복된 경우
        Failed = 3,             // 실패
        ProcessNotFound = 4     // Update 시 해당 PID를 찾을 수 없음
    }

    public ProcessInsertResult InsertResult { get; set; }

    public void ByteToData(byte[] bytes, ref int index)
    {
        InsertResult = (ProcessInsertResult)TCPConverter.ToInt(bytes, ref index);
    }

    public void DataToByte(byte[] bytes, ref int index)
    {
        TCPConverter.SetBytes(bytes, (int)InsertResult, ref index);
    }
}

// 공정 등록을 처리하는 메인 클래스
public class FuncInsertProcessInfo : FuncInfo
{
    public ProcessInsertRequest RequestData { get; set; } = new ProcessInsertRequest();
    public ProcessInsertResponse ResponseData { get; private set; } = new ProcessInsertResponse();

    protected override int GetFuncID()
    {
        return (int)FuncID.PROCESS_INFO_INSERT; // 공정 등록에 대한 FuncID
    }

    public override bool DBQuery()
    {
        bool isUpdate = RequestData.PID >= 0;

        // Update인 경우 해당 PID가 존재하는지 확인
        if (isUpdate)
        {
            string checkProcessQuery = $@"
                SELECT COUNT(*) AS cnt
                FROM process_info
                WHERE pid = {RequestData.PID}";

            DataTable checkProcessResult = Query(checkProcessQuery);
            if (checkProcessResult == null || ToInt(checkProcessResult, "cnt") == 0)
            {
                ResponseData.InsertResult = ProcessInsertResponse.ProcessInsertResult.ProcessNotFound;
                Common.DEBUG("해당 PID의 공정을 찾을 수 없습니다.");
                return false;
            }
        }

        // QR 정보 중복 여부 확인
        string checkInputQRQuery = $@"
            SELECT COUNT(*) AS cnt
            FROM process_info
            WHERE (input_qr = '{RequestData.InputQR}' OR output_qr = '{RequestData.InputQR}')
            {(isUpdate ? $"AND pid != {RequestData.PID}" : "")}";

        DataTable checkInputQRResult = Query(checkInputQRQuery);
        if (checkInputQRResult != null && ToInt(checkInputQRResult, "cnt") > 0)
        {
            ResponseData.InsertResult = ProcessInsertResponse.ProcessInsertResult.DuplicateInputQR;
            Common.DEBUG("Input QR 정보가 이미 사용 중입니다.");
            return false;
        }

        string checkOutputQRQuery = $@"
            SELECT COUNT(*) AS cnt
            FROM process_info
            WHERE (input_qr = '{RequestData.OutputQR}' OR output_qr = '{RequestData.OutputQR}')
            {(isUpdate ? $"AND pid != {RequestData.PID}" : "")}";

        DataTable checkOutputQRResult = Query(checkOutputQRQuery);
        if (checkOutputQRResult != null && ToInt(checkOutputQRResult, "cnt") > 0)
        {
            ResponseData.InsertResult = ProcessInsertResponse.ProcessInsertResult.DuplicateOutputQR;
            Common.DEBUG("Output QR 정보가 이미 사용 중입니다.");
            return false;
        }

        // 파라미터를 JSON 문자열로 변환
        string parametersJson = JsonUtility.ToJson(new ProcessParameters 
        { 
            parameters = RequestData.Parameters?.Select(p => new ProcessParameter 
            { 
                key = p.Key, 
                value = p.Value
            }).ToList() ?? new List<ProcessParameter>() 
        });

        string query;
        if (isUpdate)
        {
            // Update 쿼리
            query = $@"
                UPDATE process_info 
                SET proc_nm = '{RequestData.ProcessName}',
                    proc_loc = '{RequestData.ProcessLocation}',
                    input_qr = '{RequestData.InputQR}',
                    output_qr = '{RequestData.OutputQR}',
                    parameters = '{parametersJson}'::jsonb
                WHERE pid = {RequestData.PID}";
        }
        else
        {
            // Insert 쿼리
            query = $@"
                INSERT INTO process_info (proc_nm, proc_loc, input_qr, output_qr, parameters, reg_dt)
                VALUES ('{RequestData.ProcessName}', '{RequestData.ProcessLocation}', 
                        '{RequestData.InputQR}', '{RequestData.OutputQR}', 
                        '{parametersJson}'::jsonb, CURRENT_TIMESTAMP)";
        }

        DataTable result = Query(query);
        if (result == null)
        {
            ResponseData.InsertResult = ProcessInsertResponse.ProcessInsertResult.Failed;
            Common.DEBUG($"공정 {(isUpdate ? "수정" : "등록")} 실패");
            return false;
        }

        ResponseData.InsertResult = ProcessInsertResponse.ProcessInsertResult.Success;
        Common.DEBUG($"공정 {(isUpdate ? "수정" : "등록")} 성공");
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
