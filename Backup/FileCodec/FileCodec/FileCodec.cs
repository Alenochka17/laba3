/*----------------------------------------------------------------------+
 |	filename:	FileCodec.cs                                            |
 |----------------------------------------------------------------------|
 |	version:	1.14                                                    |
 |	revision:   01/03/2007	11:30                                       |
 |  author:		�������� ���� ��������� (DrAF)                         |
 |	e-mail:		draf@mail.ru                                            |
 |	purpose:	������ � �������                                        |
 +----------------------------------------------------------------------+
 |  ����������� ��������� ������ � RAID-�������� �����                  |
 +----------------------------------------------------------------------*/

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.IO;

namespace RecoveryStar
{
    /// <summary>
    /// ������� ���������� ��������� ����������� ������
    /// </summary>
    /// <param name="progress">������� ��������</param>
    public delegate void OnUpdateFileCodingProgressHandler(double progress);

    /// <summary>
    /// ������� ���������� �������� ����������� ������
    /// </summary>
    public delegate void OnFileCodingFinishHandler();
    
    /// <summary>
    /// ����� ��� ����������� ������ � RAID-�������� �����
    /// </summary>
    public class FileCodec
    {
        #region Construction & Destruction

        public FileCodec()
        {
            // �������������� ��������� ������ ��� �������� (����������) ����� �����
            // � ���������� ������
            _eFileNamer = new FileNamer();

            // ���� � ������ ��� ��������� ��-��������� ������
            _path = "";

            // �������������� ��� ����� ��-���������            
            _filename = "NONAME";

            // �������� ������ ��������� �������� ���������?
            _finished = true;

            // ��������� ����������� ���������?
            _processedOK = false;

            // ��-��������� ��������������� ������� ���������
            _threadPriority = 0;

            // �������������� ������� ����������� ��������� ������
            _exitEvent = new ManualResetEvent[] { new ManualResetEvent(false) };

            // �������������� c������ ����������� ��������� ������
            _executeEvent = new ManualResetEvent[] { new ManualResetEvent(false) };

            // �������������� c������ "�����������" ����� ��������
            _wakeUpEvent = new ManualResetEvent[] { new ManualResetEvent(false) };

            // �������, ��������������� �� ���������� ���������
            _finishedEvent = new ManualResetEvent[] { new ManualResetEvent(true) };
        }

        #endregion Construction & Destruction

        #region Delegates

        /// <summary>
        /// ������� ���������� �������� ������������ ������� "F"
        /// </summary>        
        public OnUpdateRSMatrixFormingProgressHandler OnUpdateRSMatrixFormingProgress;

        /// <summary>
        /// ������� ���������� �������� ������������ ������� "F"
        /// </summary>
        public OnRSMatrixFormingFinishHandler OnRSMatrixFormingFinish;

        /// <summary>
        /// ������� ���������� ��������� ����������� ������
        /// </summary>        
        public OnUpdateFileCodingProgressHandler OnUpdateFileCodingProgress;

        /// <summary>
        /// ������� ���������� �������� ��������� (����������) �����
        /// </summary>
        public OnFileCodingFinishHandler OnFileCodingFinish;

        #endregion Delegates

        #region Public Operations

        /// <summary>
        /// ���������� ���������� ��� �������������� �������� �����
        /// </summary>
        /// <param name="path">���� � ������ ��� ���������</param>
        /// <param name="filename">��� �����, �������� ����������� ��������� �����</param>        
        /// <param name="dataCount">���������� �������� �����</param>
        /// <param name="eccCount">���������� ����� ��� ��������������</param> 
        /// <param name="runAsSeparateThread">��������� � ��������� ������?</param>
        /// <returns>��������� ���� ��������</returns>
        public bool StartToEncode(String path, String filename, int dataCount, int eccCount, bool runAsSeparateThread)
        {            
            // ���� ����� ����������� ����� �������� - �� ��������� ��������� ������        
            if (InProcessing)
            {
                return false;
            }
            
            // ���������� ���� ������������ ���������� ����� �������� ������
            _processedOK = false;

            // ���������� ��������� ����������� ��������� ����������-������
            _finished = false;
            
            // ��������� ���� � ������ ��� ���������
            if (path == null)
            {
                _path = "";

            } else
            {
                // ���������� ��������� ���� �� "_path" � ������,
                // ���� ���� ���� �������� ������ ���
                _path = _eFileNamer.GetPath(path);
            }

            if (filename == null)
            {
                // ���������� ��������� ����������� ��������� ����������-������
                _finished = true;

                // ������������� ������� ���������� ���������
                _finishedEvent[0].Set();

                return false;
            }
                        
            // ���������� ��������� ��������� ����� ����� �� "_filename" � ������,
            // ���� ���� ���� �������� ������ ���
            _filename = _eFileNamer.GetShortFilename(filename);
                                 
            // ��������� �� ���������� ������������
            if (
                    (eccCount > dataCount)
                 ||
                    (eccCount <= 0)
                 ||
                    ((dataCount + eccCount) > 65535)
                )
            {
                // ���������� ��������� ����������� ��������� ����������-������
                _finished = true;

                // ������������� ������� ���������� ���������
                _finishedEvent[0].Set();

                return false;
            }
             
            // ��������� ���������� �������� �����
            _dataCount = dataCount;

            // ��������� ���������� ����� ��� ��������������
            _eccCount = eccCount;
                       
            // ���������, ��� ����� ������ �����������
            _exitEvent[0].Reset();
            _executeEvent[0].Set();
            _finishedEvent[0].Reset();

            // ���� �������, ��� �� ��������� ������ � ��������� ������,
            // ��������� � ������
            if (!runAsSeparateThread)
            {
                // �������� ����� ����� � ���������� ����� ��� ��������������
                Encode();

                // ���������� ��������� ���������
                return _processedOK;
            }

            // ������� ����� ����������� ������...
            _thrFileCodingProc = new Thread(new ThreadStart(Encode));

            //...����� ���� ��� ���...
            _thrFileCodingProc.Name = "FileCodec.Encode()";

            //...������������� ��������� ��������� ������...
            _thrFileCodingProc.Priority = _threadPriority;
            
            //...� ��������� ���
            _thrFileCodingProc.Start();

            // ��������, ��� ��� ���������
            return true;
        }
        
        /// <summary>
        /// �������������� ��������� �������� �����
        /// </summary>
        /// <param name="path">���� � ������ ��� ���������</param>
        /// <param name="filename">��� �����, �������� ����������� ��������� �����</param>                
        /// <param name="dataCount">���������� �������� �����</param>
        /// <param name="eccCount">���������� ����� ��� ��������������</param>        
        /// <param name="volList">������ ������� ��������� �����</param>        
        /// <param name="runAsSeparateThread">��������� � ��������� ������?</param>
        /// <returns>��������� ���� ��������</returns>               
        public bool StartToDecode(String path, String filename, int dataCount, int eccCount, int[] volList, bool runAsSeparateThread)
        {
            // ���� ����� ������������� ����� �������� - �� ��������� ��������� ������        
            if (InProcessing)
            {
                return false;
            }
            
            // ���������� ���� ������������ ���������� ����� �������� ������
            _processedOK = false;

            // ���������� ��������� ����������� ��������� ����������-������
            _finished = false;
            
            // ��������� ���� � ������ ��� ���������
            if (path == null)
            {
                _path = "";

            } else
            {
                // ���������� ��������� ���� �� "_path" � ������,
                // ���� ���� ���� �������� ������ ���
                _path = _eFileNamer.GetPath(path);
            }

            if (filename == null)
            {
                // ���������� ��������� ����������� ��������� ����������-������
                _finished = true;

                // ������������� ������� ���������� ���������
                _finishedEvent[0].Set();

                return false;
            }

            // ���������� ��������� ��������� ����� ����� �� "_filename" � ������,
            // ���� ���� ��� �������� ������ ���
            _filename = _eFileNamer.GetShortFilename(filename);
            
            // ��������� �� ���������� ������������
            if (
                    (eccCount > dataCount)
                 ||
                    (eccCount <= 0)
                 ||
                    ((dataCount + eccCount) > 65535)
                )
            {
                // ���������� ��������� ����������� ��������� ����������-������
                _finished = true;

                // ������������� ������� ���������� ���������
                _finishedEvent[0].Set();

                return false;
            }
            
            // ��������� ���������� �������� �����
            _dataCount = dataCount;

            // ��������� ���������� ����� ��� ��������������
            _eccCount = eccCount;

            // ��������� ������ ������� ��������� �����
            _volList = volList;
            
            // ���������, ��� ����� ������ �����������
            _exitEvent[0].Reset();
            _executeEvent[0].Set();
            _finishedEvent[0].Reset();

            // ���� �������, ��� �� ��������� ������ � ��������� ������,
            // ��������� � ������
            if (!runAsSeparateThread)
            {
                // ���������� ������������������ ������ � ��������������� �������� �����
                Decode();

                // ���������� ��������� ���������
                return _processedOK;
            }
       
            // ������� ����� �������������� �������� �����...
            _thrFileCodingProc = new Thread(new ThreadStart(Decode));

            //...����� ���� ��� ���...
            _thrFileCodingProc.Name = "FileCodec.Decode()";

            //...������������� ��������� ��������� ������...
            _thrFileCodingProc.Priority = _threadPriority;
           
            //...� ��������� ���
            _thrFileCodingProc.Start();

            // ��������, ��� ��� ���������
            return true;
        }
        
        /// <summary>
        /// ����� ��������� ������
        /// </summary>
        public void Stop()
        {
            // ���������, ��� ����� ��������� ������ �� ������ �����������            
            _executeEvent[0].Set(); // ������� ������� ����� "�����"...
            _wakeUpEvent[0].Set();  //...�, �����, ������� � "�������"...     
            _exitEvent[0].Set();    //...� ���������, ��� ��������� �����
        }

        /// <summary>
        /// ���������� ������ ��������� �� �����
        /// </summary>
        public void Pause()
        {
            // ������ ��������� �� �����
            _executeEvent[0].Reset(); // ������� ������������� ����� "�����"...
            _wakeUpEvent[0].Set();    //...�, �����, ������� � "�������"...            
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
        /// ����������� ������������������ ������
        /// </summary>
        private void Encode()
        {
            // ������� RAID-�������� ����� ����-��������
            if (_eRSRaidEncoder == null)
            {
                _eRSRaidEncoder = new RSRaidEncoder(_dataCount, _eccCount);

            } else
            {
                _eRSRaidEncoder.SetConfig(_dataCount, _eccCount);
            }

            // ������������� �� ���������
            _eRSRaidEncoder.OnUpdateRSMatrixFormingProgress = OnUpdateRSMatrixFormingProgress;
            _eRSRaidEncoder.OnRSMatrixFormingFinish = OnRSMatrixFormingFinish;

            // ��������� ���������� RAID-��������� ������ ����-��������
            if (_eRSRaidEncoder.Prepare(true))
            {
                // ���� �������� ���������� ���������� ������ ����-�������� � ������
                while (true)
                {
                    // ���� �� ���������� �������������� ������� "_executeEvent",
                    // �� ������������ �����, ����� �� ��������� ��������� �� ����� -                       
                    if (!ManualResetEvent.WaitAll(_executeEvent, 0, false))
                    {
                        //...���������������� ������ ��������������� ���������...                      
                        _eRSRaidEncoder.Pause();

                        //...� ���� ��������
                        ManualResetEvent.WaitAll(_executeEvent);
                        
                        // � ����� ����������, ���������, ��� ��������� ������ ������������                            
                        _eRSRaidEncoder.Continue();
                    }

                    // ���� ����� �� ������������� �������...
                    int eventIdx = ManualResetEvent.WaitAny(new ManualResetEvent[] { _wakeUpEvent[0], _exitEvent[0], _eRSRaidEncoder.FinishedEvent[0] });

                    //...���� �������� ������ � ����, ����� ���������� -
                    // ��������� �� ����� ��������, �.�. �����������
                    // ����� ����������� �� �����...
                    if (eventIdx == 0)
                    {
                        //...�������������� ������� �������, ����������� ��� ����������
                        _wakeUpEvent[0].Reset();

                        continue;
                    }

                    //...���� �������� ������ � ������ �� ���������...
                    if (eventIdx == 1)
                    {
                        //...������������� �������������� ��������
                        _eRSRaidEncoder.Stop();

                        // ��������� �� ��, ��� ��������� ���� ��������
                        _processedOK = false;

                        // ���������� ��������� ����������� ��������� ����������-������
                        _finished = true;

                        // ������������� ������� ���������� ���������
                        _finishedEvent[0].Set();

                        return;
                    }

                    //...���� �������� ������ � ���������� ��������� ��������� ����������...
                    if (eventIdx == 2)
                    {
                        //...������� �� ����� �������� ���������� (����� � ����� � while(true)!)
                        break;
                    }

                } // while(true)

            } else
            {
                // ���������� ���� ������������ ����������
                _processedOK = false;

                // ���������� ��������� ����������� ��������� ����������-������
                _finished = true;

                // ������������� ������� ���������� ���������
                _finishedEvent[0].Set();

                return;
            }

            // ����� ����� ��� �� ��������, ������������� �� ��������� ��������,
            // ��������, ��� "�� ����������". ���������� ��������� ��������,
            // �� ���� - ���� �������.
            for (int i = 0; i < 9000; i++)
            {
                if (!_eRSRaidEncoder.Finished)
                {
                    Thread.Sleep(100);
                }
            }

            // ���� ����� �� ����������������� ��������� - �������...
            if (!_eRSRaidEncoder.ConfigIsOK)
            {
                //...�������� �� ������
                _processedOK = false;

                // ���������� ��������� ����������� ��������� ����������-������
                _finished = true;

                // ������������� ������� ���������� ���������
                _finishedEvent[0].Set();

                return;
            }

            // �������� ������ ��� ������� ������� � �������� ������ ������
            int[] source = new int[_dataCount];
            int[] target = new int[_eccCount];

            // "���������" ����� ��� ������ � ������ ����
            byte[] smallBuff = new byte[2];

            // �������� ������ ��� ������� �������� �������
            BufferedStream[] fileStreamSourceArr = new BufferedStream[_dataCount];
            BufferedStream[] fileStreamTargetArr = new BufferedStream[_eccCount];
                       
            try
            {               
                // ��� ����� ��� ���������
                String filename;

                // ����� �������� ����
                int volNum;

                // �������������� ������� �������� ������� �������� �����
                for (volNum = 0; volNum < _dataCount; volNum++)
                {
                    // ��������� �������������� ��� �����,...
                    filename = _filename;

                    //...����������� ��� � ���������� ������...
                    _eFileNamer.Pack(ref filename, volNum, _dataCount, _eccCount);

                    //...��������� ������ ��� �����...
                    filename = _path + filename;

                    //...� ��������� �� ��� ������ ������� �������� �����
                    fileStreamSourceArr[volNum] = new BufferedStream(new FileStream(filename, FileMode.Open, System.IO.FileAccess.Read));
                }

                // �������������� ������� �������� ������� ����� ��� ��������������
                for (int eccNum = 0; volNum < (_dataCount + _eccCount); volNum++, eccNum++)
                {
                    // ��������� �������������� ��� �����...
                    filename = _filename;

                    //...����������� ��� � ���������� ������...
                    _eFileNamer.Pack(ref filename, volNum, _dataCount, _eccCount);

                    //...��������� ������ ��� �����...
                    filename = _path + filename;

                    //...� ��������� �� ��� ������ �������� �������� �����
                    fileStreamTargetArr[eccNum] = new BufferedStream(new FileStream(filename, FileMode.Create, System.IO.FileAccess.Write));                   
                }

                // ��������� �������� ������, ������� �������� �������� ������� ���������
                // ����� ��� ��������� ����������
                int progressMod1 = (int)(fileStreamSourceArr[0].Length / 200);

                // ���� ������ ����� ����, �� ����������� ��� �� �������� "1", �����
                // �������� ��������� �� ������ �������� (���� ����� ���������)
                if (progressMod1 == 0)
                {
                    progressMod1 = 1;
                }
                                                                   
                // �������� �� ����� ������� ��� ���� � �������� �������
                for (int i = 0; i < (fileStreamSourceArr[0].Length / 2); i++)
                {
                    // ��������� ������ �������� ������ ������ ������� �������� �����
                    for (int j = 0; j < _dataCount; j++)
                    {
                        // ������ ���� ���� �� �������� ������
                        fileStreamSourceArr[j].Read(smallBuff, 0, 2);

                        // ���������� ������� ���� �������� byte � int
                        source[j] = (int)(((uint)(smallBuff[0] << 0) & 0x00FF)
                                           |
                                          ((uint)(smallBuff[1] << 8) & 0xFF00));
                    }

                    // �������� ������ (�������� ���� ��� ��������������)
                    _eRSRaidEncoder.Process(source, ref target);

                    // ������� � ����� ������ ���������� ������ (ecc)
                    for (int j = 0; j < _eccCount; j++)
                    {            
                        // ���������� ���������� ������ �������� �� ��� (int16 �� ��� byte)
                        smallBuff[0] = (byte)((target[j] >> 0) & 0x00FF);
                        smallBuff[1] = (byte)((target[j] >> 8) & 0x00FF);

                        // ������ ����� ���� ���� � �������� �����
                        fileStreamTargetArr[j].Write(smallBuff, 0, 2);
                    }

                    // ������� �������� ��������� ����� ������ �������
                    if (
                            ((i % progressMod1) == 0)
                         &&
                            (OnUpdateFileCodingProgress != null)
                       )
                    {
                        OnUpdateFileCodingProgress(((double)(i + 1) / (double)fileStreamSourceArr[0].Length) * 200.0);
                    }

                    // � ������, ���� ��������� ���������� �� �����, ������� "_executeEvent"
                    // ����� ��������, � ����� �� ����� ������ �� ��� ���������
                    ManualResetEvent.WaitAll(_executeEvent);

                    // ���� �������, ��� ��������� ����� �� ������ - �������
                    if (ManualResetEvent.WaitAll(_exitEvent, 0, false))
                    {
                        // ��������� ������� �������� ������
                        for (int j = 0; j < _dataCount; j++)
                        {
                            if (fileStreamSourceArr[j] != null)
                            {
                                fileStreamSourceArr[j].Close();
                                fileStreamSourceArr[j] = null;
                            }
                        }

                        // ��������� �������� �������� ������
                        for (int j = 0; j < _eccCount; j++)
                        {
                            if (fileStreamTargetArr[j] != null)
                            {
                                fileStreamTargetArr[j].Close();
                                fileStreamTargetArr[j] = null;
                            }
                        }

                        // ��������� �� ��, ��� ��������� ���� ��������
                        _processedOK = false;

                        // ���������� ��������� ����������� ��������� ����������-������
                        _finished = true;

                        // ������������� ������� ���������� ���������
                        _finishedEvent[0].Set();

                        return;
                    }
                }
                
                // ��������� ������� �������� ������
                for (int i = 0; i < _dataCount; i++)
                {
                    if (fileStreamSourceArr[i] != null)
                    {
                        fileStreamSourceArr[i].Close();
                        fileStreamSourceArr[i] = null;
                    }
                }

                // ��������� �������� �������� ������
                for (int i = 0; i < _eccCount; i++)
                {
                    if (fileStreamTargetArr[i] != null)
                    {
                        fileStreamTargetArr[i].Flush();
                        fileStreamTargetArr[i].Close();
                        fileStreamTargetArr[i] = null;
                    }
                }

                // ��������, ��� ��������� ������ ���������
                if (OnFileCodingFinish != null)
                {
                    OnFileCodingFinish();
                }
            }

            // ���� ���� ���� �� ���� ���������� - ��������� �������� ����� �
            // �������� �� ������
            catch (IOException e)
            {
                // ��������� ������� �������� ������
                for (int i = 0; i < _dataCount; i++)
                {
                    if (fileStreamSourceArr[i] != null)
                    {
                        fileStreamSourceArr[i].Close();
                        fileStreamSourceArr[i] = null;
                    }
                }

                // ��������� �������� �������� ������
                for (int i = 0; i < _eccCount; i++)
                {
                    if (fileStreamTargetArr[i] != null)
                    {
                        fileStreamTargetArr[i].Close();
                        fileStreamTargetArr[i] = null;
                    }
                }

                // ��������� �� ��, ��� ��������� ������ ������ � �������
                _processedOK = false;

                // ���������� ��������� ����������� ��������� ����������-������
                _finished = true;

                // ������������� ������� ���������� ���������
                _finishedEvent[0].Set();

                return;
            }           

            // ��������� �� ��, ��� ��������� ���� ����������� ���������
            _processedOK = true;

            // ���������� ��������� ����������� ��������� ����������-������
            _finished = true;

            // ������������� ������� ���������� ���������
            _finishedEvent[0].Set();
        }

        /// <summary>
        /// ������������� ������������������ ������
        /// </summary>
        private void Decode()
        {     
            // ������ ������������ �������� �����
            int[] damagedVolList = new int[_dataCount];

            // ������� ���������� ������������ �����
            int damagedVolCount = 0;

            // ������� RAID-�������� ������� ����-��������            
            if (_eRSRaidDecoder == null)
            {
                _eRSRaidDecoder = new RSRaidDecoder(_dataCount, _eccCount, _volList);

            } else
            {
                _eRSRaidDecoder.SetConfig(_dataCount, _eccCount, _volList);
            }
            
            // ������������� �� ���������
            _eRSRaidDecoder.OnUpdateRSMatrixFormingProgress = OnUpdateRSMatrixFormingProgress;
            _eRSRaidDecoder.OnRSMatrixFormingFinish = OnRSMatrixFormingFinish;
                                            
            // ��������� ���������� RAID-��������� �������� ����-��������           
            if (_eRSRaidDecoder.Prepare(true))
            {
                // ���� �������� ���������� ���������� �������� ����-�������� � ������                   
                while (true)
                {
                    // ���� �� ���������� �������������� ������� "_executeEvent",
                    // �� ������������ �����, ����� �� ��������� ��������� �� ����� -                       
                    if (!ManualResetEvent.WaitAll(_executeEvent, 0, false))
                    {
                        //...���������������� ������ ��������������� ���������...                      
                        _eRSRaidDecoder.Pause();

                        //...� ���� ��������
                        ManualResetEvent.WaitAll(_executeEvent);

                        // � ����� ����������, ���������, ��� ��������� ������ ������������                            
                        _eRSRaidDecoder.Continue();
                    }

                    // ���� ����� �� ������������� �������...
                    int eventIdx = ManualResetEvent.WaitAny(new ManualResetEvent[] { _wakeUpEvent[0], _exitEvent[0], _eRSRaidDecoder.FinishedEvent[0] });

                    //...���� �������� ������ � ����, ����� ���������� -
                    // ��������� �� ����� ��������, �.�. �����������
                    // ����� ����������� �� �����...
                    if (eventIdx == 0)
                    {
                        //...�������������� ������� �������, ����������� ��� ����������
                        _wakeUpEvent[0].Reset();

                        continue;
                    }

                    //...���� �������� ������ � ������ �� ���������...
                    if (eventIdx == 1)
                    {
                        //...������������� �������������� ��������
                        _eRSRaidDecoder.Stop();

                        // ��������� �� ��, ��� ��������� ���� ��������
                        _processedOK = false;

                        // ���������� ��������� ����������� ��������� ����������-������
                        _finished = true;

                        // ������������� ������� ���������� ���������
                        _finishedEvent[0].Set();

                        return;
                    }

                    //...���� �������� ������ � ���������� ��������� ��������� ����������...
                    if (eventIdx == 2)
                    {
                        //...������� �� ����� �������� ���������� (����� � ����� � while(true)!)
                        break;
                    }

                } // while(true)

            } else
            {
                // ���������� ���� ������������ ����������
                _processedOK = false;

                // ���������� ��������� ����������� ��������� ����������-������
                _finished = true;

                return;
            }

            // ����� ����� ��� �� ��������, ������������� �� ��������� ��������,
            // ��������, ��� "�� ����������". ���������� ��������� ��������,
            // �� ���� - ���� �������.
            for (int i = 0; i < 9000; i++)
            {
                if (!_eRSRaidDecoder.Finished)
                {
                    Thread.Sleep(100);
                }
            }               
                  
            // �������� ������ ��� ������� ������� � �������� ������ ������
            int[] source = new int[_dataCount];
            int[] target = new int[_dataCount];

            // "���������" ����� ��� ������ � ������ ����
            byte[] smallBuff = new byte[2];

            // �������� ������ ��� ������� �������� �������
            BufferedStream[] fileStreamSourceArr = new BufferedStream[_dataCount];
            BufferedStream[] fileStreamTargetArr = new BufferedStream[_dataCount];
            
            try
            {
                // ��� ����� ��� ���������
                String filename;

                // ��������� ������� �������� ������
                for (int i = 0; i < _dataCount; i++)
                {
                    // ��������� �������������� ��� �����,...
                    filename = _filename;

                    //...����������� ��� � ���������� ������...
                    _eFileNamer.Pack(ref filename, _volList[i], _dataCount, _eccCount);

                    //...��������� ������ ��� �����...
                    filename = _path + filename;

                    //...���������� ���� �� ������� �����...                    
                    if (!File.Exists(filename))
                    {
                        // ��������� �� ��, ��� ��������� ������ ������ � �������
                        _processedOK = false;

                        // ���������� ��������� ����������� ��������� ����������-������
                        _finished = true;

                        // ������������� ������� ���������� ���������
                        _finishedEvent[0].Set();

                        return;
                    }

                    //...� ��������� �� ��� ������ ������� �������� �����
                    fileStreamSourceArr[i] = new BufferedStream(new FileStream(filename, FileMode.Open, System.IO.FileAccess.Read));
                }
                                                                                             
                // ����������, ����� �� �������� ����� (�� ������ "_volList") ����������,
                // � ����� - ���
                for (int i = 0; i < _volList.Length; i++)
                {
                    // ��������� ����� �������� ����
                    int currVol = Math.Abs(_volList[i]);

                    // ���� ������ ��� �� �������� ��������...
                    if (currVol >= _dataCount)
                    {
                        //...���������, �� ������ ����
                        damagedVolList[damagedVolCount++] = i;
                    }
                }
                                                                
                // ��������� �������� �������� ������ ��� ������������ ������
                for (int i = 0; i < damagedVolCount; i++)
                {
                    // ��������� �������������� ��� �����,...
                    filename = _filename;

                    //...����������� ��� � ���������� ������...
                    _eFileNamer.Pack(ref filename, damagedVolList[i], _dataCount, _eccCount);

                    //...��������� ������ ��� �����...
                    filename = _path + filename;

                    //...� ��������� �� ��� ������ �������� �������� �����
                    fileStreamTargetArr[damagedVolList[i]] = new BufferedStream(new FileStream(filename, FileMode.Create, System.IO.FileAccess.Write));                    
                }

                // ��������� �������� ������, ������� �������� �������� ������� ���������
                // ����� ��� ��������� ����������
                int progressMod1 = (int)(fileStreamSourceArr[0].Length / 200);

                // ���� ������ ����� ����, �� ����������� ��� �� �������� "1", �����
                // �������� ��������� �� ������ �������� (���� ����� ���������)
                if (progressMod1 == 0)
                {
                    progressMod1 = 1;
                }
               
                // �������� �� ����� ������� ��� ���� � �������� �������
                for (int i = 0; i < ((fileStreamSourceArr[0].Length - 8) / 2); i++)
                {
                    // ��������� ������ �������� ������ ������ ������� �������� �����
                    for (int j = 0; j < _dataCount; j++)
                    {
                        // ������ ���� ���� �� �������� ������
                        fileStreamSourceArr[j].Read(smallBuff, 0, 2);

                        // ���������� ������� ���� �������� byte � int
                        source[j] = (int)(((uint)(smallBuff[0] << 0) & 0x00FF)
                                           |
                                          ((uint)(smallBuff[1] << 8) & 0xFF00));
                    }

                    // ���������� ������ (�������� ������ ���������� ������ �������� �����)
                    _eRSRaidDecoder.Process(source, ref target);

                    // ������� ���������� �������� ������� �������� ������
                    for (int j = 0; j < damagedVolCount; j++)
                    {
                        // ���������� ���������� ������ �������� �� ��� (int16 �� ��� byte)
                        smallBuff[0] = (byte)((target[damagedVolList[j]] >> 0) & 0x00FF);
                        smallBuff[1] = (byte)((target[damagedVolList[j]] >> 8) & 0x00FF);

                        // ������ ����� ���� ���� � �������� �����
                        fileStreamTargetArr[damagedVolList[j]].Write(smallBuff, 0, 2);
                    }

                    // ������� �������� ��������� ����� ������ �������
                    if (
                            ((i % progressMod1) == 0)
                         &&
                            (OnUpdateFileCodingProgress != null)
                       )
                    {
                        OnUpdateFileCodingProgress(((double)(i + 1) / (double)fileStreamSourceArr[0].Length) * 200.0);
                    }

                    // � ������, ���� ��������� ���������� �� �����, ������� "_executeEvent"
                    // ����� ��������, � ����� �� ����� ������ �� ��� ���������
                    ManualResetEvent.WaitAll(_executeEvent);

                    // ���� �������, ��� ��������� ����� �� ������ - �������
                    if (ManualResetEvent.WaitAll(_exitEvent, 0, false))
                    {
                        // ��������� ������� �������� ������
                        for (int j = 0; j < _dataCount; j++)
                        {
                            if (fileStreamSourceArr[j] != null)
                            {
                                fileStreamSourceArr[j].Close();
                                fileStreamSourceArr[j] = null;
                            }
                        }

                        // ��������� �������� �������� ������
                        for (int j = 0; j < _eccCount; j++)
                        {
                            if (fileStreamTargetArr[j] != null)
                            {
                                fileStreamTargetArr[j].Close();
                                fileStreamTargetArr[j] = null;
                            }
                        }

                        // ��������� �� ��, ��� ��������� ���� ��������
                        _processedOK = false;

                        // ���������� ��������� ����������� ��������� ����������-������
                        _finished = true;

                        // ������������� ������� ���������� ���������
                        _finishedEvent[0].Set();

                        return;
                    }
                }

                // ��������� ������� �������� ������
                for (int i = 0; i < _dataCount; i++)
                {
                    if (fileStreamSourceArr[i] != null)
                    {
                        fileStreamSourceArr[i].Close();
                        fileStreamSourceArr[i] = null;
                    }
                }

                // ��������� �������� �������� ������ ���:
                for (int i = 0; i < damagedVolCount; i++)
                {
                    // ������� ����� ��������� 8 ���� ������ �������� CRC-64,
                    // �, �����, ��������� ����.
                    if (fileStreamTargetArr[damagedVolList[i]] != null)
                    {
                        fileStreamTargetArr[damagedVolList[i]].Write(new byte[8], 0, 8);
                        fileStreamTargetArr[damagedVolList[i]].Flush();
                        fileStreamTargetArr[damagedVolList[i]].Close();
                        fileStreamTargetArr[damagedVolList[i]] = null;
                    }
                }
                              
                // ��������, ��� ��������� ������ ���������
                if (OnFileCodingFinish != null)
                {
                    OnFileCodingFinish();
                }
            }

            // ���� ���� ���� �� ���� ���������� - ��������� �������� ����� �
            // �������� �� ������
            catch (IOException e)
            {
                // ��������� ������� �������� ������
                for (int i = 0; i < _dataCount; i++)
                {
                    if (fileStreamSourceArr[i] != null)
                    {
                        fileStreamSourceArr[i].Close();
                        fileStreamSourceArr[i] = null;
                    }
                }

                // ��������� �������� �������� ������
                for (int i = 0; i < damagedVolCount; i++)
                {
                    if (fileStreamTargetArr[damagedVolList[i]] != null)
                    {
                        fileStreamTargetArr[damagedVolList[i]].Flush();
                        fileStreamTargetArr[damagedVolList[i]].Close();
                        fileStreamTargetArr[damagedVolList[i]] = null;
                    }                    
                }

                // ��������� �� ��, ��� ��������� ������ ������ � �������
                _processedOK = false;

                // ���������� ��������� ����������� ��������� ����������-������
                _finished = true;

                // ������������� ������� ���������� ���������
                _finishedEvent[0].Set();

                return;
            }

            // ��������� �� ��, ��� ��������� ���� ����������� ���������
            _processedOK = true;

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
                        (_thrFileCodingProc != null)
                     &&
                        (
                                (_thrFileCodingProc.ThreadState == ThreadState.Running)
                            ||
                                (_thrFileCodingProc.ThreadState == ThreadState.WaitSleepJoin)
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
        /// ��������� ������ ����������� ���������?"
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
                       (_thrFileCodingProc != null)
                     &&
                       (_thrFileCodingProc.IsAlive)
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
                    _thrFileCodingProc.Priority = _threadPriority;

                    if (_eRSRaidEncoder != null)
                    {
                        _eRSRaidEncoder.ThreadPriority = value;
                    }

                    if (_eRSRaidDecoder != null)
                    {
                        _eRSRaidDecoder.ThreadPriority = value;
                    }
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
        
        // ������ ��� �������� (����������) ����� � ���������� ������
        private FileNamer _eFileNamer;

        // RAID-�������� ����� ����-��������
        private RSRaidEncoder _eRSRaidEncoder;

        // RAID-�������� ������� ����-��������
        private RSRaidDecoder _eRSRaidDecoder;
              
        // ���� � ������ ��� ���������
        private String _path;

        // ��� �����, �������� ����������� ��������� �����
        private String _filename;
       
        // ���������� �������� �����
        private int _dataCount;

        // ���������� ����� ��� ��������������
        private int _eccCount;

        // ������, ����������� �� ������ �����
        private int[] _volList;

        // �������� ������ ��������� �������� ���������?
        private bool _finished;

        // ��������� ����������� ���������?
        private bool _processedOK;
        
        // ����� ����������� ������
        private Thread _thrFileCodingProc;

        // ��������� �������� ��������� (����������) �����
        private ThreadPriority _threadPriority;

        // ������� ����������� ��������� ������
        private ManualResetEvent[] _exitEvent;

        // ������� ����������� ��������� ������
        private ManualResetEvent[] _executeEvent;

        // ������� "�����������" ����� ��������
        private ManualResetEvent[] _wakeUpEvent;

        // �������, ��������������� �� ���������� ���������
        private ManualResetEvent[] _finishedEvent;

        #endregion Data

    } // class FileCodec
}