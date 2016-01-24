/*----------------------------------------------------------------------+
 |	filename:	CRC-64.cs                                               |
 |----------------------------------------------------------------------|
 |	version:	1.14                                                    |
 |	revision:   01/03/2007	11:52                                       |
 |  author:		�������� ���� ��������� (DrAF)                         |
 |	e-mail:		draf@mail.ru                                            |
 |	purpose:	�������� ����������� ������                             |
 +----------------------------------------------------------------------+
 |  ���������� ���������������� �������� ����������� ����� (CRC-64)     |
 +----------------------------------------------------------------------*/

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.IO;

namespace RecoveryStar
{
    /// <summary>
    /// ������� ���������� ��������� �������� ����������� �����
    /// </summary>
    /// <param name="progress">������� ��������</param>
    public delegate void OnUpdateFileIntegrityCheckProgressHandler(double progress);

    /// <summary>
    /// ������� ���������� �������� �������� ����������� �����
    /// </summary>
    public delegate void OnFileIntegrityCheckFinishHandler();

    /// <summary>
    /// ����� ������� CRC-64
    /// </summary>
    public class CRC64
    {
        #region Construction & Destruction

        /// <summary>
        /// ����������� ������ ������� CRC-64
        /// </summary>
        public CRC64()
        {
            // �������������� ������� ������� CRC-64
            CRC64Init();
        }

        #endregion Construction & Destruction

        #region Public Operations

        /// <summary>
        /// ���������� CRC-64 � ���������� ��������� ���������
        /// </summary>
        /// <param name="CRC64">���������� ��������� �������� CRC-64</param>
        /// <param name="source">������ �������� ������</param>
        /// <param name="offset">�������� � ������� �������� ������</param>
        /// <param name="length">����� ������� ��� ���������� CRC-64</param>
        /// <returns>����������� �������� CRC-64</returns>
        public UInt64 Process(UInt64 CRC64, byte[] source, int offset, int length)
        {
            for (int i = offset; i < (offset + length); i++)
            {
                CRC64 = ((CRC64 >> 8) & 0x00FFFFFFFFFFFFFF) ^ _CRC64Table[(0x00000000000000FF & CRC64) ^ source[i]];
            }

            // ��������� ����� ���������� �������������� �������� ��� ������������ ��������
            // ���������, ����� ��� ������ �������������� ��������� 64 ����    
            for (int i = 0; i < (length % 8); i++)
            {
                CRC64 = ((CRC64 >> 8) & 0x00FFFFFFFFFFFFFF) ^ _CRC64Table[0x00000000000000FF & CRC64];
            }
           
            return CRC64;
        }

        /// <summary>
        /// ���������� CRC-64
        /// </summary>        
        /// <param name="source">������ �������� ������</param>
        /// <param name="offset">�������� � ������� �������� ������</param>
        /// <param name="length">����� ������� ��� ���������� CRC-64</param>
        /// <returns>����������� �������� CRC-64</returns>
        public UInt64 Process(byte[] source, int offset, int length)
        {
            return Process(0xFFFFFFFFFFFFFFFF, source, offset, length);
        }

        #endregion Public Operations

        #region Private Operations

        /// <summary>
        /// ������������� ������� ������� CRC-64
        /// </summary>
        private void CRC64Init()
        {
            // �������� ������ ��� ������� ������� CRC-64
            _CRC64Table = new UInt64[_CRC64TableSize];

            // ���������� ������� CRC-64
            for (int i = 0; i < _CRC64TableSize; i++)
            {
                UInt64 c = (UInt64)i;

                for (int j = 0; j < 8; j++)
                {
                    if ((c & 1) != 0)
                    {
                        c = (c >> 1) ^ _CRC64GenPoly;

                    } else
                    {
                        c = (c >> 1);
                    }
                }

                // ����� ������������ �������� � ������
                _CRC64Table[i] = c;
            }
        }

        #endregion Private Operations

        #region Constants

        // ������ ������� ��� ������� CRC-64
        private const int _CRC64TableSize = 256;

        // ������������ ������� ��� CRC-64
        private const UInt64 _CRC64GenPoly = 0xC96C5795D7870F42;                                              

        #endregion Constants

        #region Data

        // ������� ��� ������� CRC-64
        private UInt64[] _CRC64Table;

        #endregion Data

    } // class CRC64

    /// <summary>
    /// ����� ���������� � �������� ����������� ����� �� ������ CRC-64
    /// </summary>
    public class FileIntegrityCheck
    {
        #region Construction & Destruction

        /// <summary>
        /// ����������� ������ �������� ����������� ������
        /// </summary>
        public FileIntegrityCheck()
        {
            // ������� ��������� ������ ������� CRC-64
            _eCRC64 = new CRC64();
            
            // �������������� ��� ����� ��-���������            
            _fullFilename = "NONAME";

            // ������ ��������� ������ (��-���������) - 64 ��
            _bufferLength = (64 * 1024 * 1024);
          
            // �������� ������ ��� �������� �����
            _buffer = new byte[_bufferLength];

            // � ���������� Int64 ���������� 8 ����
            _crc64Arr = new byte[8];

            // �������� ������ ��������� �������� ���������?
            _finished = true;

            // ��������� ����������� ���������?
            _processedOK = false;

            // ��-��������� ��������������� ������� ���������
            _threadPriority = 0;

            // �������������� ������� ����������� ��������� �����
            _exitEvent = new ManualResetEvent[] { new ManualResetEvent(false) };
            
            // �������������� c������ ����������� ��������� �����
            _executeEvent = new ManualResetEvent[] { new ManualResetEvent(false) };

            // �������, ��������������� �� ���������� ���������
            _finishedEvent = new ManualResetEvent[] { new ManualResetEvent(true) };
        }

        #endregion Construction & Destruction

        #region Delegates

        /// <summary>
        /// ������� ���������� ��������� �������� ����������� �����
        /// </summary>        
        public OnUpdateFileIntegrityCheckProgressHandler OnUpdateFileIntegrityCheckProgress;

        /// <summary>
        /// ������� ���������� �������� �������� ����������� �����
        /// </summary>
        public OnFileIntegrityCheckFinishHandler OnFileIntegrityCheckFinish;

        #endregion Delegates

        #region Public Operations

        /// <summary>
        /// ����� ������� ������ ��������� ���������� � ������ CRC64 � ����� �����
        /// </summary>
        /// <param name="fullFilename">��� ����� ��� ���������</param>
        /// <param name="runAsSeparateThread">��������� � ��������� ������?</param>
        /// <returns>��������� ���� ��������</returns>
        public bool StartToWriteCRC64(String fullFilename, bool runAsSeparateThread)
        {
            // ���� ����� ���������� CRC-64 �������� - �� ��������� ��������� ������        
            if (InProcessing)
            {
                return false;
            }

            // ���������� ���� ������������ ���������� ����� �������� ������
            _processedOK = false;

            // ���������� ��������� ����������� ��������� ����������-������
            _finished = false;
                        
            if (fullFilename == null)
            {
                // ���������� ��������� ����������� ��������� ����������-������
                _finished = true;

                return false;
            }

            // ���� �������� ���� �� ����������, �������� �� ������
            if (!File.Exists(fullFilename))
            {
                // ���������� ��������� ����������� ��������� ����������-������
                _finished = true;

                return false;            
            }
                        
            // ��������� ��� �����
            _fullFilename = fullFilename;

            // ���������, ��� ����� ������ �����������
            _exitEvent[0].Reset();
            _executeEvent[0].Set();
            _finishedEvent[0].Reset();

            // ���� �������, ��� �� ��������� ������ � ��������� ������,
            // ��������� � ������
            if (!runAsSeparateThread)
            {
                // ��������� ���������� � ������ CRC-64 � ����� �����
                WriteCRC64();
                
                // ���������� ��������� ���������
                return _processedOK;
            }
                        
            // ������� ����� ���������� � ������ CRC-64...
            _thrFileIntegrityCheck = new Thread(new ThreadStart(WriteCRC64));

            //...����� ���� ��� ���...
            _thrFileIntegrityCheck.Name = "FileIntegrityCheck.WriteCRC64()";
                            
            //...������������� ��������� ��������� ������...
            _thrFileIntegrityCheck.Priority = _threadPriority;
                     
            //...� ��������� ���
            _thrFileIntegrityCheck.Start();
  
            // ��������, ��� ��� ���������
            return true;
        }

        /// <summary>
        /// ����� ������� ������ ��������� �������� CRC64, ����������� � ����� �����
        /// </summary>
        /// <param name="fullFilename">��� ����� ��� ���������</param>
        /// <param name="runAsSeparateThread">��������� � ��������� ������?</param>
        /// <returns>��������� ���� ��������</returns>
        public bool StartToCheckCRC64(String fullFilename, bool runAsSeparateThread)
        {
            // ���� ����� ���������� CRC-64 �������� - �� ��������� ��������� ������        
            if (InProcessing)
            {
                return false;
            }

            // ���������� ���� ������������ ���������� ����� �������� ������
            _processedOK = false;

            // ���������� ��������� ����������� ��������� ����������-������
            _finished = false;
                        
            if (fullFilename == null)
            {
                // ���������� ��������� ����������� ��������� ����������-������
                _finished = true;

                // ������������� ������� ���������� ���������
                _finishedEvent[0].Set();

                return false;
            }

            // ���� �������� ���� �� ����������, �������� �� ������
            if (!File.Exists(fullFilename))
            {
                // ���������� ��������� ����������� ��������� ����������-������
                _finished = true;

                // ������������� ������� ���������� ���������
                _finishedEvent[0].Set();

                return false;
            }

            // ��������� ��� �����
            _fullFilename = fullFilename;

            // ���������, ��� ����� ������ �����������
            _exitEvent[0].Reset();
            _executeEvent[0].Set();
            _finishedEvent[0].Reset();

            // ���� �������, ��� �� ��������� ������ � ��������� ������,
            // ��������� � ������
            if (!runAsSeparateThread)
            {
                // ��������� ���������� � �������� �������� CRC-64
                CheckCRC64();

                // ���������� ��������� ���������
                return _processedOK;
            }
            
            // ������� ����� ���������� � �������� CRC-64...
            _thrFileIntegrityCheck = new Thread(new ThreadStart(CheckCRC64));

            //...����� ���� ��� ���...
            _thrFileIntegrityCheck.Name = "FileIntegrityCheck.CheckCRC64()";
                
            //...������������� ��������� ��������� ������...
            _thrFileIntegrityCheck.Priority = _threadPriority;
                        
            //...� ��������� ���
            _thrFileIntegrityCheck.Start();

            // ��������, ��� ��� ���������
            return true;
        }

        /// <summary>
        /// ����� ��������� ������
        /// </summary>
        public void Stop()
        {
            // ���������, ��� ����� ��������� ������ �� ������ �����������
            _executeEvent[0].Set(); // ��� ���������� ��� ��������������� ������ � �����
            _exitEvent[0].Set();
        }

        /// <summary>
        /// ���������� ������ ��������� �� �����
        /// </summary>
        public void Pause()
        {
            // ������ ��������� �� �����
            _executeEvent[0].Reset();            
        }

        /// <summary>
        /// ������ ������ ��������� � �����
        /// </summary>
        public void Continue()
        {
            // ������� ��������� c �����
            _executeEvent[0].Set();            
        }
        
        #endregion Public Operations

        #region Private Operations

        /// <summary>
        /// ���������� CRC-64 ���������� �����
        /// </summary>
        /// <param name="fullFilename">��� ����� ��� ���������</param>
        /// <param name="endOffset">��������, "����������������" ��� ����������� � ����� �����</param>        
        /// <returns>��������� ���� ��������</returns>
        private bool CalcCRC64(String fullFilename, int endOffset)
        {
            // ��������� ��������� ������
            FileStream eFileStream = null;

            try
            {                
                // ��������� �������� ����� �� ������
                eFileStream = new FileStream(fullFilename, FileMode.Open, System.IO.FileAccess.Read);
                
                // ������ ����� ����� ��� ���������
                Int64 fileLength = eFileStream.Length;

                // ���� ������ ����� ������ �������� - ��� ������
                if ((fileLength - endOffset) == 0)
                {
                    return false;
                }
                
                // ������ ���������� �������� �������� �� ���������� CRC-64
                Int64 nIterations = ((fileLength - endOffset) / _bufferLength);
                
                // ��������� �������, �� ����������� ��� ���������� �������� ��������
                int iterRest = (int)((fileLength - endOffset) - (nIterations * _bufferLength));

                // ��������� �������� ������, ������� �������� �������� ������� ���������
                // ����� ��� ��������� ���������� ��� ����� �� "i"
                int progressMod1 = (int)(nIterations / 100);

                // ���� ������ ����� ����, �� ����������� ��� �� �������� "1", �����
                // �������� ��������� �� ������ �������� (���� ����� ���������)
                if (progressMod1 == 0)
                {
                    progressMod1 = 1;
                }
            
                // ������ � ��������������� �������� (������ � �������� ���������)
                for (Int64 i = 0; i < nIterations; i++)
                {
                    // ������ ������ � �����
                    eFileStream.Read(_buffer, 0, _bufferLength);

                    // ��������� �������� CRC-64 �� ��������� ������ ��� ���������� ��������� �����
                    _crc64 = _eCRC64.Process(_crc64, _buffer, 0, _bufferLength);

                    // ������� �������� ���������                  
                    if (
                            ((i % progressMod1) == 0)
                         &&
                            (OnUpdateFileIntegrityCheckProgress != null)
                        )
                    {
                        OnUpdateFileIntegrityCheckProgress(((double)(i + 1) / (double)nIterations) * 100.0);
                    }

                    // � ������, ���� ��������� ���������� �� �����, ������� "_executeEvent"
                    // ����� ��������, � ����� �� ����� ������ �� ��� ���������
                    ManualResetEvent.WaitAll(_executeEvent);

                    // ���� �������, ��� ��������� ����� �� ������ - �������
                    if (ManualResetEvent.WaitAll(_exitEvent, 0, false))
                    {
                        if (eFileStream != null)
                        {
                            eFileStream.Close();                            
                            eFileStream = null;
                        }

                        return false;
                    }                    
                }

                // ������������ ������� (���� �� ����)
                if (iterRest > 0)
                {
                    // ������ ������ � �����
                    eFileStream.Read(_buffer, 0, iterRest);

                    // ��������� �������� CRC-64 �� ��������� ������ ��� ���������� ��������� �����
                    _crc64 = _eCRC64.Process(_crc64, _buffer, 0, iterRest);                   
                }

                // ��������, ��� ��������� ����� ���������
                if (OnFileIntegrityCheckFinish != null)
                {
                    OnFileIntegrityCheckFinish();
                }
            }

            // ���� ���� ���� �� ���� ���������� - ��������� �������� ����� �
            // �������� �� ������
            catch (IOException e)
            {
                if (eFileStream != null)
                {
                    eFileStream.Close();
                    eFileStream = null;
                }

                return false;                               
            }

            // ���� ����� �� ������� ������� ���� - ���������� �� ���� - ��������,
            // ��� ��� ���������
            if (eFileStream != null)
            {
                eFileStream.Close();
                eFileStream = null;
            }

            return true;
        }

        /// <summary>
        /// ���������� � ������ � ����� ����� �������� CRC-64
        /// </summary>                
        private void WriteCRC64()
        {
            // ��������� �������� CRC-64
            _crc64 = 0xFFFFFFFFFFFFFFFF;

            // ���� ���������� CRC-64 � ������� ����� ������ ���������...
            if (CalcCRC64(_fullFilename, 0))
            {
                // ��������� ��������� ������
                FileStream eFileStream = null;

                // ��������� ������ ������ ������� ����� ������ � �������� ����
                BinaryWriter eBinaryWriter = null;

                try
                {   
                    //...��������� �������� ����� �� ������...
                    eFileStream = new FileStream(_fullFilename, FileMode.Append, System.IO.FileAccess.Write);

                    //...� ���������� ��� ��� ������������� ���������� ������ ������
                    // ������� ����� ������ � �������� ����
                    eBinaryWriter = new BinaryWriter(eFileStream);

                    // ������������ �� ����� �����...
                    eBinaryWriter.Seek(0, SeekOrigin.End);

                    //...� ����� � ��� ����� ����������� �������� CRC-64,...
                    eBinaryWriter.Write(_crc64);

                    //...������� �������� �����...
                    eBinaryWriter.Flush();

                    //...� ��������� ����
                    if (eBinaryWriter != null)
                    {
                        eBinaryWriter.Close();
                        eBinaryWriter = null;
                    }                    
                }

                // ���� ���� ���� �� ���� ���������� - ��������� �������� ����� �
                // �������� �� ������
                catch (IOException e)
                {
                    // ��������� ����
                    if (eBinaryWriter != null)
                    {
                        eBinaryWriter.Close();
                        eBinaryWriter = null;
                    }

                    // ���������� ���� ������������ ����������
                    _processedOK = false;

                    // ���������� ��������� ����������� ��������� ����������-������
                    _finished = true;

                    // ������������� ������� ���������� ���������
                    _finishedEvent[0].Set();

                    return;
                }

                // ��������� �� ��, ��� CRC-64 � ������� ����� ���� ��������� ���������
                _processedOK = true;

            } else
            {
                // ��������� �� ��, ��� CRC-64 � ������� ����� ���� ��������� �����������
                _processedOK = false;
            }

            // ���������� ��������� ����������� ��������� ����������-������
            _finished = true;

            // ������������� ������� ���������� ���������
            _finishedEvent[0].Set();
        }

        /// <summary>
        /// ���������� � �������� �������� CRC-64, ����������� � ����� �����
        /// </summary>        
        private void CheckCRC64()
        {
            // ��������� �������� CRC-64
            _crc64 = 0xFFFFFFFFFFFFFFFF;

            // ����������� � ����� �������� CRC-64:
            UInt64 crc64;

            // ��������� ��������� ������
            FileStream eFileStream = null;

            // ���� ���������� CRC-64 � ������� ����� �� ��������� "8" ������ ���������...           
            if (CalcCRC64(_fullFilename, 8))
            {                
                try
                {
                    //...�� ��������� �������� ����� �� ������...
                    eFileStream = new FileStream(_fullFilename, FileMode.Open, System.IO.FileAccess.Read);

                    //...� ��������� ���������������� �� ����� �����, ����� ������� �������� CRC-64
                    eFileStream.Seek(((Int64)eFileStream.Length - 8), SeekOrigin.Begin);

                    // ������ ����������� � ����� ����� �������� CRC-64...
                    eFileStream.Read(_crc64Arr, 0, 8);

                    //...� ��������� ����
                    if (eFileStream != null)
                    {
                        eFileStream.Close();
                        eFileStream = null;
                    }                    
                }

                // ���� ���� ���� �� ���� ���������� - ��������� �������� ����� �
                // �������� �� ������
                catch (IOException e)
                {
                    if (eFileStream != null)
                    {
                        eFileStream.Close();
                        eFileStream = null;
                    }

                    // ���������� ���� ������������ ����������
                    _processedOK = false;

                    // ���������� ��������� ����������� ��������� ����������-������
                    _finished = true;

                    // ������������� ������� ���������� ���������
                    _finishedEvent[0].Set();

                    return;
                }

                // ������ ����������� ������ byte[] � Int64
                crc64  = 0x0000000000000000;              
                crc64 |= ((UInt64)_crc64Arr[0] << 0)  & (0x00000000000000FF);
                crc64 |= ((UInt64)_crc64Arr[1] << 8)  & (0x000000000000FF00);
                crc64 |= ((UInt64)_crc64Arr[2] << 16) & (0x0000000000FF0000);
                crc64 |= ((UInt64)_crc64Arr[3] << 24) & (0x00000000FF000000);
                crc64 |= ((UInt64)_crc64Arr[4] << 32) & (0x000000FF00000000);
                crc64 |= ((UInt64)_crc64Arr[5] << 40) & (0x0000FF0000000000);
                crc64 |= ((UInt64)_crc64Arr[6] << 48) & (0x00FF000000000000);
                crc64 |= ((UInt64)_crc64Arr[7] << 56) & (0xFF00000000000000);
         
                // ���� ����������� �������� CRC-64 �� ������� � �����������,
                // ��������� �� ������
                if (_crc64 != crc64)
                {
                    _processedOK = false;

                } else
                {
                    _processedOK = true;
                }
                                               
            } else
            {
                // ��������� �� ��, ��� ������ CRC-64 ����� ������ �� ���������
                _processedOK = false;
            }

            // ���������� ��������� ����������� ��������� ����������-������
            _finished = true;

            // ������������� ������� ���������� ���������
            _finishedEvent[0].Set();
        }

        #endregion Private Operations

        #region Public Properties

        /// <summary>
        /// ��������� �������� "���� ��������������?"
        /// </summary>
        public bool InProcessing
        {
            get
            {
                if (
                        (_thrFileIntegrityCheck != null)
                     &&
                        (
                                (_thrFileIntegrityCheck.ThreadState == ThreadState.Running)
                            ||
                                (_thrFileIntegrityCheck.ThreadState == ThreadState.WaitSleepJoin)
                        )                        
                    )
                {
                    return true;

                } else
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// ��������� �������� "��������� ������ �������� ���������
        /// (����� ���������� ��������� ����������-������)?"
        /// </summary>   
        public bool Finished
        {
            get
            {
                // ���� ����� �� ����� ���������� - ���������� ��������
                if (!InProcessing)
                {
                    return _finished;

                } else
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// ��������� �������� "CRC-64 ����� ��������� ���������?"
        /// </summary>
        public bool ProcessedOK
        {
            get
            {
                // ���� ����� �� ����� ���������� - ���������� ��������
                if (!InProcessing)
                {
                    return _processedOK;
                    
                } else
                {
                    return false;
                }
            }
        }
        
        /// <summary>
        /// ��� �����, ������������� ���������
        /// </summary>
        public String fullFilename
        {
            get
            {
                // ���� ����� �� ����� ���������� - ���������� ��������...
                if (!InProcessing)
                {
                    return _fullFilename;

                } else
                {
                    //...� ����� �������� �� ��������
                    return "-1";
                }               
            }           
        }

        /// <summary>
        /// ��� �����, ������������� ���������
        /// </summary>
        public UInt64 CRC64
        {
            get
            {
                // ���� ����� �� ����� ���������� - ���������� �������� ��������...
                if (!InProcessing)
                {
                    return _crc64;

                } else
                {
                    ///...� ����� �������� �� ��������
                    return 0xFFFFFFFFFFFFFFFF;
                }              
            }            
        }
                
        /// <summary>
        /// ������ ��������� ������
        /// </summary>
        public int BufferLength
        {
            get
            {
                // ���� ����� �� ����� ���������� - ���������� ��������...
                if (!InProcessing)
                {
                    return _bufferLength;

                } else
                {
                    //...� ����� �������� �� ��������
                    return -1;
                }
            }

            set
            {
                // ���� ����� �� ����� ���������� - ������������� ��������...
                if (!InProcessing)
                {
                    //... �� ������ ���� ��� �� �������� ����������� ������ ������ - 8 ��
                    if (value >= (8 * 1024))
                    {
                        _bufferLength = value;
                    }
                }
            }
        }

        /// <summary>
        /// ��������� ��������
        /// </summary>
        public int ThreadPriority
        {
            get
            {
                return (int)_threadPriority;
            }

            set
            {
                if (
                        (_thrFileIntegrityCheck != null)
                     &&
                        (_thrFileIntegrityCheck.IsAlive)
                    )
                {
                    switch (value)
                    {
                        default:
                        case 0:
                        {
                            _threadPriority = System.Threading.ThreadPriority.Lowest;

                            break;
                        }

                        case 1:
                        {
                            _threadPriority = System.Threading.ThreadPriority.BelowNormal;

                            break;
                        }

                        case 2:
                        {
                            _threadPriority = System.Threading.ThreadPriority.Normal;

                            break;
                        }

                        case 3:
                        {
                            _threadPriority = System.Threading.ThreadPriority.AboveNormal;

                            break;
                        }

                        case 4:
                        {
                            _threadPriority = System.Threading.ThreadPriority.Highest;

                            break;
                        }
                    }
                    
                    // ������������� ��������� ��������� ��������
                    _thrFileIntegrityCheck.Priority = _threadPriority;                                    
                }                
            }
        }

        /// <summary>
        /// �������, ��������������� �� ���������� ���������
        /// </summary>
        public ManualResetEvent[] FinishedEvent
        {
            get
            {
                return _finishedEvent;
            }
        }

        #endregion Public Properties
      
        #region Data

        // ��������� ������ ������� CRC-64
        private CRC64 _eCRC64;

        // ��� ����� ��� ���������
        private String _fullFilename;

        // �������� �����        
        private byte[] _buffer;

        // ������ ��������� ������ (64 ��)
        private int _bufferLength;

        // ����� ��� �������������� ��������� ������������� CRC-64
        private byte[] _crc64Arr;

        // �������� CRC-64, ��������������� "_fullFilename"
        private UInt64 _crc64;

        // �������� ������ ��������� �������� ���������?
        private bool _finished;

        // CRC-64 ��������� ���������?
        private bool _processedOK;
        
        // ����� ���������� CRC-64 ����� "_fullFilename" � ����������� ���������� � "_processedOK"
        private Thread _thrFileIntegrityCheck;

        // ��������� �������� ������� CRC-64
        private ThreadPriority _threadPriority;

        // ������� ����������� ��������� �����
        private ManualResetEvent[] _exitEvent;

        // ������� ����������� ��������� �����
        private ManualResetEvent[] _executeEvent;

        // �������, ��������������� �� ���������� ���������
        private ManualResetEvent[] _finishedEvent;
     
        #endregion Data

    } // class FileIntegrityCheck
}
