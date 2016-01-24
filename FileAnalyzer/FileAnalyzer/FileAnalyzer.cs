/*----------------------------------------------------------------------+
 |	filename:	FileAnalyzer.cs                                         |
 |----------------------------------------------------------------------|
 |	version:	1.14                                                    |
 |	revision:   02/03/2007	08:42                                       |
 |  author:		�������� ���� ��������� (DrAF)                         |
 |	e-mail:		draf@mail.ru                                            |
 |	purpose:	�������� ����������� ������                             |
 +----------------------------------------------------------------------+
 |  ���������� �������� ����������� ������ ������-����� (CRC-64)        |
 +----------------------------------------------------------------------*/

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.IO;

namespace RecoveryStar
{
    /// <summary>
    /// ������� ���������� ��������� �������� ����������� ������
    /// </summary>
    /// <param name="progress">������� ��������</param>
    public delegate void OnUpdateFileAnalyzeProgressHandler(double progress);

    /// <summary>
    /// ������� ���������� �������� �������� ����������� ������
    /// </summary>
    public delegate void OnFileAnalyzeFinishHandler();

    /// <summary>
    /// ������� ��������� ���������� ����������� ������������ ������
    /// </summary>
    /// <param name="percOfDamage">������� ����������� �� ��������� � ������ ����� �����</param>
    /// <param name="percOfAltEcc">������� ������ (�� ��������� � ������ �����) �� ����� ��� ��������������</param>
    public delegate void OnGetDamageStatHandler(double percOfDamage, double percOfAltEcc);

    /// <summary>
    /// ����� �������� ����������� ������ ������-�����
    /// </summary>
    public class FileAnalyzer
    {        
        #region Construction & Destruction

        /// <summary>
        /// ����������� ������ �������� ����������� ������ ������
        /// </summary>
        public FileAnalyzer()
        {
            // ������ ��� �������� (����������) ����� ����� � ���������� ������
            _eFileNamer = new FileNamer();

            // ������� ��������� ������ �������� ����������� ������ ������
            _eFileIntegrityCheck = new FileIntegrityCheck();
        
            // ���� � ������ ��� ��������� ��-��������� ������
            _path = "";
            
            // �������������� ��� ����� ��-���������            
            _filename = "NONAME";

            // ���������� ��� ���� ��� �������������� ������� �������������
            _allEccVolsOK = false;
            
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
        /// ������� ���������� ��������� �������� ����������� ������
        /// </summary>
        public OnUpdateFileAnalyzeProgressHandler OnUpdateFileAnalyzeProgress;

        /// <summary>
        /// ������� ���������� �������� �������� ����������� ������
        /// </summary>
        public OnFileAnalyzeFinishHandler OnFileAnalyzeFinish;

        /// <summary>
        /// ������� ��������� ���������� ����������� ������������ ������
        /// </summary>        
        public OnGetDamageStatHandler OnGetDamageStat;

        #endregion Delegates

        #region Public Operations

        /// <summary>
        /// ����� ������� ������ ��������� ���������� � ������ CRC64 � ����� ������
        /// </summary>
        /// <param name="path">���� � ������ ��� ���������</param>
        /// <param name="filename">��� ����� ��� ���������</param>        
        /// <param name="dataCount">������������ ���������� �������� �����</param>
        /// <param name="eccCount">������������ ���������� ����� ��� ��������������</param>
        /// <param name="runAsSeparateThread">��������� � ��������� ������?</param>
        /// <returns>��������� ���� ��������</returns>
        public bool StartToWriteCRC64(String path, String filename, int dataCount, int eccCount, bool runAsSeparateThread)
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
                // ��������� CRC-64 ��� ������� �� ������ ������
                WriteCRC64();

                // ���������� ��������� ���������
                return _processedOK;
            }
                        
            // ������� ����� ���������� � ������ CRC-64...
            _thrFileAnalyze = new Thread(new ThreadStart(WriteCRC64));

            //...����� ���� ��� ���...
            _thrFileAnalyze.Name = "FileAnalyzer.WriteCRC64()";
                            
            //...������������� ��������� ��������� ������...
            _thrFileAnalyze.Priority = _threadPriority;
                     
            //...� ��������� ���
            _thrFileAnalyze.Start();
  
            // ��������, ��� ��� ���������
            return true;
        }

        /// <summary>
        /// ����� ������� ������ ��������� �������� CRC64, ����������� � �����
        /// ������� �� ������ ������, � �������������� ������ ��������� ����� "volList",
        /// ������� ����� ����������� ��������� ��� �������������� ������
        /// </summary>
        /// <param name="path">���� � ������ ��� ���������</param>
        /// <param name="filename">��� ����� ��� ���������</param>        
        /// <param name="dataCount">������������ ���������� �������� �����</param>
        /// <param name="eccCount">������������ ���������� ����� ��� ��������������</param>
        /// <param name="runAsSeparateThread">��������� � ��������� ������?</param>
        /// <returns>��������� ���� ��������</returns>
        public bool StartToAnalyzeCRC64(String path, String filename, int dataCount, int eccCount, bool runAsSeparateThread)
        {
            // ���� ����� ���������� CRC-64 �������� - �� ��������� ��������� ������        
            if (InProcessing)
            {
                return false;
            }

            // ���������� ��� ���� ��� �������������� ������� �������������
            _allEccVolsOK = false;

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
                // ��������� � ��������� CRC-64 ��� ������� �� ������ ������ � �����������
                // �������� VolList
                AnalyzeCRC64();

                // ���������� ��������� ���������
                return _processedOK;
            }
            
            // ������� ����� ���������� � �������� CRC-64...
            _thrFileAnalyze = new Thread(new ThreadStart(AnalyzeCRC64));

            //...����� ���� ��� ���...
            _thrFileAnalyze.Name = "FileAnalyzer.AnalyzeCRC64()";
                
            //...������������� ��������� ��������� ������...
            _thrFileAnalyze.Priority = _threadPriority;
                        
            //...� ��������� ���
            _thrFileAnalyze.Start();

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
        /// ���������� � ������ � ����� ������ �������� CRC-64
        /// </summary>                
        private void WriteCRC64()
        {   
            // ��������� �������� ������, ������� �������� �������� ������� ���������
            // ����� ��� ��������� ���������� ��� ����� �� "i"
            int progressMod1 = ((_dataCount + _eccCount) / 100);

            // ���� ������ ����� ����, �� ����������� ��� �� �������� "1", �����
            // �������� ��������� �� ������ �������� (���� ����� ���������)
            if (progressMod1 == 0)
            {
                progressMod1 = 1;
            }
            
            // ���������� ��������� ��� ����
            for (int volNum = 0; volNum < (_dataCount + _eccCount); volNum++)
            {     
                // ��������� �������������� ��� �����
                String filename = _filename;

                // �������� ��� ��������� ����� � ���������� �����
                _eFileNamer.Pack(ref filename, volNum, _dataCount, _eccCount);

                // ��������� ������ ��� �����
                filename = _path + filename;
                
                // ���������� ���������� CRC-64 ��� ������� �����
                if (_eFileIntegrityCheck.StartToWriteCRC64(filename, true))
                {
                    // ���� �������� ���������� ��������� �����
                    while (true)
                    {                                            
                        // ���� �� ���������� �������������� ������� "_executeEvent",
                        // �� ������������ �����, ����� �� ��������� ��������� �� ����� -                       
                        if (!ManualResetEvent.WaitAll(_executeEvent, 0, false))
                        {
                            //...���������������� ������ ��������������� ���������...                      
                            _eFileIntegrityCheck.Pause();

                            //...� ���� ��������
                            ManualResetEvent.WaitAll(_executeEvent);
                            
                            // � ����� ����������, ���������, ��� ��������� ������ ������������                            
                            _eFileIntegrityCheck.Continue();
                        }

                        // ���� ����� �� ������������� �������...
                        int eventIdx = ManualResetEvent.WaitAny(new ManualResetEvent[] { _wakeUpEvent[0], _exitEvent[0], _eFileIntegrityCheck.FinishedEvent[0] });

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
                            _eFileIntegrityCheck.Stop();

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

                // � ����� � ��������� �������� ���������� �������� �������
                // ���������� ��������� ������ ���������, ��������� �������
                // ����������� � ���� ������. ����� ��� �� ��������, ��
                // ������������� �� ��������� ��������, ��������, ���
                // "�� ����������". ���������� ��������� ��������,
                // �� ���� - ���� �������.
                for (int i = 0; i < 9000; i++)
                {
                    if (!_eFileIntegrityCheck.Finished)
                    {
                        Thread.Sleep(100);
                    }
                }

                // ���� 15 ����� �������� �������� �������� ������� �� ������� � ���������
                // ���������� - ��� ������
                if (!_eFileIntegrityCheck.ProcessedOK)
                {
                    // ��������� �� ��, ��� ��������� �� ���� ��������� ���������
                    _processedOK = false;

                    // ���������� ��������� ����������� ��������� ����������-������
                    _finished = true;

                    // ������������� ������� ���������� ���������
                    _finishedEvent[0].Set();

                    return;
                }

                // ������� �������� ���������
                if (
                        ((volNum % progressMod1) == 0)
                     &&
                        (OnUpdateFileAnalyzeProgress != null)
                    )
                {
                    OnUpdateFileAnalyzeProgress(((double)(volNum + 1) / (double)(_dataCount + _eccCount)) * 100.0);
                }

                // � ������, ���� ��������� ���������� �� �����, ������� "_executeEvent"
                // ����� ��������, � ����� �� ����� ������ �� ��� ���������
                ManualResetEvent.WaitAll(_executeEvent);

                // ���� �������, ��� ��������� ����� �� ������ - �������
                if (ManualResetEvent.WaitAll(_exitEvent, 0, false))
                {
                    // ��������� �� ��, ��� ��������� ���� ��������
                    _processedOK = false;

                    // ���������� ��������� ����������� ��������� ����������-������
                    _finished = true;

                    // ������������� ������� ���������� ���������
                    _finishedEvent[0].Set();

                    return;
                }      
            }
                       
            // �������� �� ��������� �������� ���������
            if (OnFileAnalyzeFinish != null)
            {
                OnFileAnalyzeFinish();
            }

            // ��������, ��� ��������� ������ ���������
            _processedOK = true;

            // ���������� ��������� ����������� ��������� ����������-������
            _finished = true;

            // ������������� ������� ���������� ���������
            _finishedEvent[0].Set();
        }
         
	    /// <summary>
        /// ���������� � �������� �������� CRC-64, ����������� � ����� �����
        /// </summary>        
        private void AnalyzeCRC64()
        {
            // ��������� ������������� ��������� �������� �� ������� ���
            // ���������� ��������� ���������
            int percOfFirstStage  = (int)((100 * _dataCount) / (_dataCount + _eccCount));
            int percOfSecondStage = (int)((100 * _eccCount)  / (_dataCount + _eccCount));

            // ������ ������ ������ �������� ���� �� ���� �������
            // (��� ������������ ��������)
            if (percOfSecondStage == 0)
            {
                percOfSecondStage = 1;
            }

            // ��������� �������� ������, ������� �������� �������� ������� ���������
            // ����� ��� ��������� ���������� ��� ����� �� "i"
            int progressMod1 = (_dataCount / percOfFirstStage);

            // ���� ������ ����� ����, �� ����������� ��� �� �������� "1", �����
            // �������� ��������� �� ������ �������� (���� ����� ���������)
            if (progressMod1 == 0)
            {
                progressMod1 = 1;
            }

            // �������� ������ ��� "_volList"
            _volList = new int[_dataCount];

            // �������� ������ ��� "_altEccList"
            int[] altEccList = new int[_eccCount];

            // ������ � ������� �����
            int volListIdx = 0;

            // ������ � ������� ����� ��� ��������������
            int altEccListIdx = 0;

            // ������� ���������� ������������ �������� �����
            int dataVolMissCount = 0;

            // ������� ���������� ��������� ����� ��� ��������������
            int eccVolPresentCount = 0;

            // ��� ����� ��� ���������
            String filename;

            // ���������� �������� ��� �������� ����
            for (int dataNum = 0; dataNum < _dataCount; dataNum++)
            {
                // ���������� ������������, ��� ������� ��� ���������
                bool dataVolIsOK = false;

                // ��������� �������������� ��� �����
                filename = _filename;
                
                // �������� ��� ��������� ����� � ���������� �����
                _eFileNamer.Pack(ref filename, dataNum, _dataCount, _eccCount);

                // ��������� ������ ��� �����
                filename = _path + filename;

                // ���� �������� ���� ����������...
                if (File.Exists(filename))
                {
                    //...- ���������� ��� ��������
                    if (_eFileIntegrityCheck.StartToCheckCRC64(filename, true))
                    {
                        // ���� �������� ���������� ��������� �����
                        while (true)
                        {
                            // ���� �� ���������� �������������� ������� "_executeEvent",
                            // �� ������������ �����, ����� �� ��������� ��������� �� ����� -                       
                            if (!ManualResetEvent.WaitAll(_executeEvent, 0, false))
                            {
                                //...���������������� ������ ��������������� ���������...                      
                                _eFileIntegrityCheck.Pause();

                                //...� ���� ��������
                                ManualResetEvent.WaitAll(_executeEvent);
                                
                                // � ����� ����������, ���������, ��� ��������� ������ ������������                            
                                _eFileIntegrityCheck.Continue();
                            }

                            // ���� ����� �� ������������� �������...
                            int eventIdx = ManualResetEvent.WaitAny(new ManualResetEvent[] { _wakeUpEvent[0], _exitEvent[0], _eFileIntegrityCheck.FinishedEvent[0] });

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
                                _eFileIntegrityCheck.Stop();

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

                    // � ����� � ��������� �������� ���������� �������� �������
                    // ���������� ��������� ������ ���������, ��������� �������
                    // ����������� � ���� ������. ����� ��� �� ��������, ��
                    // ������������� �� ��������� ��������, ��������, ���
                    // "�� ����������". ���������� ��������� ��������,
                    // �� ���� - ���� �������.
                    for (int i = 0; i < 9000; i++)
                    {
                        if (!_eFileIntegrityCheck.Finished)
                        {
                            Thread.Sleep(100);
                        }
                    }

                    // ���������, ��� �������� ��� ���������
                    if (_eFileIntegrityCheck.ProcessedOK)
                    {
                        dataVolIsOK = true;
                    }

                    // ������� �������� ���������
                    if (
                            ((dataNum % progressMod1) == 0)
                         &&
                            (OnUpdateFileAnalyzeProgress != null)
                        )
                    {
                        OnUpdateFileAnalyzeProgress(((double)(dataNum + 1) / (double)(_dataCount + _eccCount)) * percOfFirstStage);
                    }

                    // � ������, ���� ��������� ���������� �� �����, ������� "_executeEvent"
                    // ����� ��������, � ����� �� ����� ������ �� ��� ���������
                    ManualResetEvent.WaitAll(_executeEvent);

                    // ���� �������, ��� ��������� ����� �� ������ - �������
                    if (ManualResetEvent.WaitAll(_exitEvent, 0, false))
                    {
                        // ��������� �� ��, ��� ��������� ���� ��������
                        _processedOK = false;

                        // ���������� ��������� ����������� ��������� ����������-������
                        _finished = true;

                        // ������������� ������� ���������� ���������
                        _finishedEvent[0].Set();

                        return;
                    }      
                }

                // ���� ������ �������� ��� �� ���������, ���������� ��� � "_volList",
                // � ����� ����������� ������� ������������ ����� � ������ �� �����
                // ������ ���� �������� "-1", ������� ������ �� ������������� �����������
                // ���� ��� ��������������
                if (dataVolIsOK)
                {
                    _volList[volListIdx++] = dataNum;

                } else
                {
                    _volList[volListIdx++] = -1;

                    // ����������� ������� ���������� ������������ �������� �����
                    dataVolMissCount++;
                }
            }

            // ���� ���������� ��� �������� ���� - �������������� � �������� ����������
            if (dataVolMissCount == _dataCount)
            {
                // ��������, ��� �������������� ������ ���������� - ���������� ���
                // �������� ����
                if (OnGetDamageStat != null)
                {
                    OnGetDamageStat(-1, -1);
                }

                // ��������� �� ��, ��� ������ �� ����� ���� �������������
                _processedOK = false;

                // ���������� ��������� ����������� ��������� ����������-������
                _finished = true;

                // ������������� ������� ���������� ���������
                _finishedEvent[0].Set();
                
                return;
            }
                               
            // ������, ����� ����� ���������� ������������ �������� �����,
            // ����� �������������� ��� ����� ��� ��������������, � ����������
            // ��������� �� ����� � ������ �����, � "�������" ��������� �
            // ������ �������������� ����� ��� ��������������

            // ��������� �������� ������, ������� �������� �������� ������� ���������
            // ����� ��� ��������� ���������� ��� ����� �� "i"
            progressMod1 = (_eccCount / percOfSecondStage);

            // ���� ������ ����� ����, �� ����������� ��� �� �������� "1", �����
            // �������� ��������� �� ������ �������� (���� ����� ���������)
            if (progressMod1 == 0)
            {
                progressMod1 = 1;
            }

            // ����������� ������ ����� ��� ��������������
            for (int eccNum = _dataCount; eccNum < (_dataCount + _eccCount); eccNum++)
            {
                // ���������� ������������, ��� ������� ��� ���������
                bool eccVolIsOK = false;

                // ��������� �������������� ��� �����
                filename = _filename;

                // �������� ��� ��������� ����� � ���������� �����
                _eFileNamer.Pack(ref filename, eccNum, _dataCount, _eccCount);

                // ��������� ������ ��� �����
                filename = _path + filename;

                // ���� �������� ���� ����������...
                if (File.Exists(filename))
                {
                    //...- ���������� ��� ��������
                    if (_eFileIntegrityCheck.StartToCheckCRC64(filename, true))
                    {
                        // ���� �������� ���������� ��������� �����
                        while (true)
                        {
                            // ���� �� ���������� �������������� ������� "_executeEvent",
                            // �� ������������ �����, ����� �� ��������� ��������� �� ����� -                       
                            if (!ManualResetEvent.WaitAll(_executeEvent, 0, false))
                            {
                                //...���������������� ������ ��������������� ���������...                      
                                _eFileIntegrityCheck.Pause();

                                //...� ���� ��������
                                ManualResetEvent.WaitAll(_executeEvent);
                                
                                // � ����� ����������, ���������, ��� ��������� ������ ������������                            
                                _eFileIntegrityCheck.Continue();
                            }

                            // ���� ����� �� ������������� �������...
                            int eventIdx = ManualResetEvent.WaitAny(new ManualResetEvent[] { _wakeUpEvent[0], _exitEvent[0], _eFileIntegrityCheck.FinishedEvent[0] });

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
                                _eFileIntegrityCheck.Stop();

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

                    // � ����� � ��������� �������� ���������� �������� �������
                    // ���������� ��������� ������ ���������, ��������� �������
                    // ����������� � ���� ������. ����� ��� �� ��������, ��
                    // ������������� �� ��������� ��������, ��������, ���
                    // "�� ����������". ���������� ��������� ��������,
                    // �� ���� - ���� �������.
                    for (int i = 0; i < 9000; i++)
                    {
                        if (!_eFileIntegrityCheck.Finished)
                        {
                            Thread.Sleep(100);
                        }
                    }

                    // ���������, ��� ��� ��� �������������� ���������
                    if (_eFileIntegrityCheck.ProcessedOK)
                    {
                        eccVolIsOK = true;
                    }

                    // ������� �������� ���������
                    if (
                            ((eccNum % progressMod1) == 0)
                         &&
                            (OnUpdateFileAnalyzeProgress != null)
                        )
                    {
                        OnUpdateFileAnalyzeProgress((((double)(eccNum + 1) / (double)(_dataCount + _eccCount)) * percOfSecondStage) + percOfFirstStage);
                    }

                    // � ������, ���� ��������� ���������� �� �����, ������� "_executeEvent"
                    // ����� ��������, � ����� �� ����� ������ �� ��� ���������
                    ManualResetEvent.WaitAll(_executeEvent);

                    // ���� �������, ��� ��������� ����� �� ������ - �������
                    if (ManualResetEvent.WaitAll(_exitEvent, 0, false))
                    {
                        // ��������� �� ��, ��� ��������� ���� ��������
                        _processedOK = false;

                        // ���������� ��������� ����������� ��������� ����������-������
                        _finished = true;

                        // ������������� ������� ���������� ���������
                        _finishedEvent[0].Set();

                        return;
                    }      
                }

                // ���� ��� ��� �������������� �������...
                if (eccVolIsOK)
                {
                    //...- ��������� ��� � ������
                    altEccList[altEccListIdx++] = eccNum;

                    // ����������� ������� ���������� ����� ��� ��������������
                    eccVolPresentCount++;

                } else
                {
                    // ���� ���������� ���� �� ���� ������������ ��� ��� ��������������,
                    // � �� ��������� � ��������� ���������������� ���� �������� �����,
                    // �� ����������� ������������ ����� ��� �������������� ���������������
                    if (dataVolMissCount == 0)
                    {
                        // �������� �� ��������� �������� ���������
                        if (OnFileAnalyzeFinish != null)
                        {
                            OnFileAnalyzeFinish();
                        }

                        // ������� ���������� ����������� (-1 ��������, ��� �������� �� ����������)
                        if (OnGetDamageStat != null)
                        {
                            OnGetDamageStat(0, -1);
                        }

                        // ��������� �� ��, ��� ������ �� ����������
                        _processedOK = true;

                        // ���������� ��������� ����������� ��������� ����������-������
                        _finished = true;

                        // ������������� ������� ���������� ���������
                        _finishedEvent[0].Set();

                        return;
                    }

                    //...� ����� ���������, ��� ��� ���������
                    altEccList[altEccListIdx++] = -1;
                }
            }

            // ���� �������� �������� ���������� ���������� ����� ��� �������������� ���������
            // �� ��������� �������� ����� ��� �������������� ������������ - ��� ���� ���
            // �������������� �������� ���������������
            if (eccVolPresentCount == _eccCount)
            {
                _allEccVolsOK = true;
            }

            // ���� ��� ������������ �������� �����, ������ �������
            // (�������������� ������������� ��� ���� ��� ��������������, �.�.
            // ����� ������, ���� �� ������������ ����� ���)
            if (dataVolMissCount == 0)
            {
                // �������� �� ��������� �������� ���������
                if (OnFileAnalyzeFinish != null)
                {
                    OnFileAnalyzeFinish();
                }

                // ������� ���������� ����������� (-1 ��������, ��� �������� �� ����������)
                if (OnGetDamageStat != null)
                {
                    OnGetDamageStat(0, -1);
                }

                // ��������� �� ��, ��� ������ �� ����������
                _processedOK = true;

                // ���������� ��������� ����������� ��������� ����������-������
                _finished = true;

                // ������������� ������� ���������� ���������
                _finishedEvent[0].Set();

                return;
            }
           
            // ���� �� �� ������ ������������ �����������...
            if (eccVolPresentCount < dataVolMissCount)               
            {
                //...������� ���������� ������� �����...
                if (OnGetDamageStat != null)
                {
                    // ��������� ����� ������� ����������� (����� �����������
                    // �������� ����� � ����� ��� �������������� ����� �� ����� ���������� �����)
                    double percOfDamage = ((double)(dataVolMissCount + (_eccCount - eccVolPresentCount)) / (double)(_dataCount + _eccCount)) * 100;

                    // ��������� ������� "��������" �������������� ����� ��� �������������� (������������
                    // ����� ������ �����). �������������� ���� - ��� ���������� �� ����, ������� ��
                    // ����������� ������������ ��� ��������������
                    double percOfAltEcc = ((double)(eccVolPresentCount - dataVolMissCount) / (double)(_dataCount + _eccCount)) * 100;

                    OnGetDamageStat(percOfDamage, percOfAltEcc);
                }

                //...� ��������� �� ��, ��� ������ �� ����� ���� �������������
                _processedOK = false;

                // ���������� ��������� ����������� ��������� ����������-������
                _finished = true;

                // ������������� ������� ���������� ���������
                _finishedEvent[0].Set();

                return;
            }
         
            // ������������ �� ������ ������ �������������� ����� ��� ��������������
            altEccListIdx = 0;
            
            // ������ ����������� �� ������� "_volList", � ������ ������� �� �������� "-1"
            // ����������� ��������� �������� �� ���������� ���������
            for (int i = 0; i < _dataCount; i++)
            {
                if (_volList[i] == -1)
                {                   
                    // ����������� �� ������� ����� ��� ��������������,
                    // �������������� �� ���������� ���� ��� ��������������
                    while (altEccList[altEccListIdx] == -1)
                    {
                        altEccListIdx++;                        
                    }

                    // ����������� �� ����� ������������� ��������� ����
                    // ��� ��� ��������������,...
                    _volList[i] = altEccList[altEccListIdx];

                    //...������ �������������� ��� �� ������ ��������������
                    altEccList[altEccListIdx] = -1;           
                }
            }
                
            // �������� �� ��������� �������� ���������
            if (OnFileAnalyzeFinish != null)
            {
                OnFileAnalyzeFinish();
            }

            // ������� ���������� �����������
            if (OnGetDamageStat != null)
            {
                // ��������� ����� ������� ����������� (����� �����������
                // �������� ����� � ����� ��� �������������� ����� �� ����� ���������� �����)
                double percOfDamage = ((double)(dataVolMissCount + (_eccCount - eccVolPresentCount)) / (double)(_dataCount + _eccCount)) * 100;

                // ��������� ������� "��������" �������������� ����� ��� �������������� (������������
                // ����� ������ �����). �������������� ���� - ��� ���������� �� ����, ������� ��
                // ����������� ������������ ��� ��������������
                double percOfAltEcc = ((double)(eccVolPresentCount - dataVolMissCount) / (double)(_dataCount + _eccCount)) * 100;
                
                OnGetDamageStat(percOfDamage, percOfAltEcc);
            }
           
            // ��������, ��� ��������� ������ ���������
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
                        (_thrFileAnalyze != null)
                     &&
                        (
                                (_thrFileAnalyze.ThreadState == ThreadState.Running)
                            ||
                                (_thrFileAnalyze.ThreadState == ThreadState.WaitSleepJoin)
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
        /// ��������� �������� "��������� ������ ���������� ���������?"
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
        /// ������ ���������� ������� ��������� �����
        /// </summary>  
        public int[] VolList
        {
            get
            {
                if (!InProcessing)
                {
                    return _volList;

                } else
                {
                    return null;
                }
            }
        }
        
        /// <summary>
        /// ��� ���� ��� �������������� ���������?
        /// </summary>  
        public bool AllEccVolsOK
        {
            get
            {
                if (!InProcessing)
                {
                    return _allEccVolsOK;

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
                        (_thrFileAnalyze != null)
                     &&
                        (_thrFileAnalyze.IsAlive)
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
                    _thrFileAnalyze.Priority = _threadPriority;
                    
                    if (_eFileIntegrityCheck != null)
                    {
                        _eFileIntegrityCheck.ThreadPriority = value;
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

        // ������ ��� �������� (����������) ����� ����� � ���������� ������
        private FileNamer _eFileNamer;

        // ��������� ������ �������� ����������� ������ ������
        private FileIntegrityCheck _eFileIntegrityCheck;
               
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

        // ��� ���� ��� �������������� ���������?
        private bool _allEccVolsOK;

        // �������� ������ ��������� �������� ���������?
        private bool _finished;

        // ��������� ������ ������ ����������� ���������?
        private bool _processedOK;
  
        // ����� �������� ����������� �����
        private Thread _thrFileAnalyze;

        // ��������� �������� �������� ����������� ������
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

    } // class FileAnalyzer
}
