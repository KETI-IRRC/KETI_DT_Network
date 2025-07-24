using System;
using System.Collections;
using System.Collections.Generic;

using System.Security.Principal;
using UnityEngine;

using System.Data;


public class FuncInfo
{
    protected const int BUFFER_SIZE = 2048;


    protected int FUNC_ID;

    protected int LINE_CD = 1, MCN_CD = 1;

    public virtual bool DBQuery() { return false; }
    public virtual void ByteToData(byte[] bytes) { }
    protected virtual byte[] DataToByte() { return null; }
    protected virtual int GetFuncID() { return 0; }

    public static FuncID ReadFuncID(byte[] data)
    {
        if (data != null && data.Length >= sizeof(int))
        {
            byte[] tmpData = new byte[data.Length];
            Array.Copy(data, tmpData, data.Length);

            return (FuncID)TCPConverter.ToInt(tmpData);
        }
        return FuncID.ERROR;
    }

    public bool IsUseable(byte[] data)
    {
        int length = sizeof(int);
        if (data != null && data.Length >= length)
            FUNC_ID = TCPConverter.ToInt(data);

        if (FUNC_ID == GetFuncID())
        {
            ByteToData(data);
            return true;
        }
        return false;
    }

    protected int GetFuncIDFromByte(byte[] bytes)
    {
        int length = sizeof(int);
        if(bytes != null && bytes.Length >= length)
            return BitConverter.ToInt32(bytes, 0);
        return 0;
    }

    public InfoTCP GetInfoTCP()
    {
        byte[] bytes = DataToByte();
        //Debug.LogWarning("infoByte : "+ bytes.Length);
        return new InfoTCP(InfoTCP.DataType.NONE, bytes);
    }


    protected DataTable Query(string query)
    {
        return DBManager.Instance.Select(query);
    }

    protected DataTable Select(int line, int mcn, string query)
    {
        return DBManager.Instance.Select(string.Format(query, line, mcn));
    }


    protected void Insert(string query)
    {
         DBManager.Instance.Insert(query);
    }

    protected bool IsNullOrEmpty(DataTable dt)
    {
        if (dt == null)
            return true;
        return dt.Rows.Count == 0;
    }

    protected byte[] FitBytes(byte[] data, int length)
    {
        byte[] returnData = new byte[length];
        Array.Copy(data, returnData, length);
        return returnData;
    }

    protected int ToInt(DataTable dt, string key, int index = 0)
    {
        return Common.DataToInt(dt, key, index);
    }
    protected string ToString(DataTable dt, string key, int index = 0)
    {
        return Common.DataToString(dt, key, index);
    }
    protected int ToInt(DataRow row, string columnName)
    {
        if (row[columnName] != DBNull.Value)
        {
            return Convert.ToInt32(row[columnName]);
        }
        return 0; // 기본값
    }

    protected string ToString(DataRow row, string columnName)
    {
        if (row[columnName] != DBNull.Value)
        {
            return row[columnName].ToString();
        }
        return string.Empty; // 기본값
    }
    public static int[] ToIntArray(DataRow row, string columnName)
    { 
        if (row[columnName] is System.Int32[] array)
        {
            return array; // 배열 그대로 반환
        }

        if (row[columnName] != DBNull.Value)
        {
            string arrayString = row[columnName].ToString();
            // Assuming the int array is stored as a comma-separated string
            string[] arrayElements = arrayString.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            int[] intArray = Array.ConvertAll(arrayElements, int.Parse);
            return intArray;
        }
        return new int[0]; // 빈 배열 반환
    }

    protected bool IsNullOrEmpty(object value)
    {
        return value == null || value == DBNull.Value || string.IsNullOrEmpty(value.ToString());
    }

}

