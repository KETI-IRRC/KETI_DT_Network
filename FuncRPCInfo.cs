using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using UnityEngine;

public class FuncRPCInfo : FuncInfo
{
    int fnc_id;
    public FuncRPCInfo(FuncID func_id)
    {
        fnc_id = (int)func_id;
    }

    public FuncRPCInfo(int func_id)
    {
        fnc_id = func_id;
    }

    protected override int GetFuncID()
    {
        return fnc_id;
    }

    public override void ByteToData(byte[] bytes)   {  }

    protected override byte[] DataToByte() 
    {
        byte[] bytes = new byte[BUFFER_SIZE];

        int index = 0;

        FUNC_ID = GetFuncID();

        TCPConverter.SetBytes(bytes, FUNC_ID, ref index);

        byte[] returnData = new byte[index];
        Array.Copy(bytes, returnData, index);
        return returnData;
    }

}
