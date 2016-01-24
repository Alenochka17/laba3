/*----------------------------------------------------------------------+
 |	filename:	RecoveryStarCore.cs                                     |
 |----------------------------------------------------------------------|
 |	version:	1.14                                                    |
 |	revision:   02/03/2007	08:46                                       |
 |  author:		�������� ���� ��������� (DrAF)                         |
 |	e-mail:		draf@mail.ru                                            |
 |	purpose:	������ � �������                                        |
 +----------------------------------------------------------------------+
 |  ���������������� ����������� �� ���� RAID-������                    |
 +----------------------------------------------------------------------*/

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.IO;

namespace RecoveryStar
{  
    /// <summary>
    /// ����� ��� ����������� ������ � RAID-�������� �������
    /// </summary>
    public class RecoveryStarCore
    {
        #region Construction & Destruction

        public RecoveryStarCore()
        {
            // ������ ��� �������� (����������) ����� ����� � ���������� ������
            _eFileNamer = new FileNamer();

             // ������ ���������� � �������� ��������� ����������� ����� CRC-64
            _eFileAnalyzer = new FileAnalyzer();
        
            // RAID-�������� �������� �����
            _eFileCodec = new FileCodec();
          
            // ������ ��������� (����������) ������ �� ����
            _eFileSplitter = new FileSplitter();

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

            // �������������� c������ "�����������" ����� ��������
            _wakeUpEvent = new ManualResetEvent[] { new ManualResetEvent(false) };
         
            // �������, ��������������� �� ���������� ���������
            _finishedEvent = new ManualResetEvent[] { new ManualResetEvent(true) };
        }

        #endregion Construction & Destruction

        #region Delegates

        /// <summary>
        /// ������� ���������� ��������� ��������� (����������) �����
        /// </summary>        
        public OnUpdateFileSplittingProgressHandler OnUpdateFileSplittingProgress;

        /// <summary>
        /// ������� ���������� �������� ��������� (����������) �����
        /// </summary>
        public OnFileSplittingFinishHandler OnFileSplittingFinish;

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
   
        /// <summary>
        /// ������� ���������� �������� �������� ����������� ������
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
        /// ���������������� ����������� ����� �� ���� RAID
        /// </summary>        
        /// <param name="fullFilename">������ ��� ����� ��� ����������������� �����������</param>
        /// <param name="dataCount">���������� �������� �����</param>
        /// <param name="eccCount">���������� ����� ��� ��������������</param>
        /// <param name="runAsSeparateThread">��������� � ��������� ������?</param>
        /// <returns>��������� ���� ��������</returns>
        public bool StartToProtect(String fullFilename, int dataCount, int eccCount, bool runAsSeparateThread)
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
                        
            // ���� ��� ����� �� �����������
            if (
                    (fullFilename == null)
                 ||
                    (fullFilename == "")
                )
            {
                // ���������� ��������� ����������� ��������� ����������-������
                _finished = true;

                // ������������� ������� ���������� ���������
                _finishedEvent[0].Set();

                return false;
            }

            // ���������� ��������� ���� �� ������� ����� �����
            _path = _eFileNamer.GetPath(fullFilename);

            // ���������� ��������� ����� �� ������� ����� �����
            _filename = _eFileNamer.GetShortFilename(fullFilename);

            // ���� �������� ���� �� ����������, �������� �� ������
            if (!File.Exists(_path + _filename))
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

            // ������������� �� ���������
            _eFileSplitter.OnUpdateFileSplittingProgress = OnUpdateFileSplittingProgress;
            _eFileSplitter.OnFileSplittingFinish = OnFileSplittingFinish;

            _eFileCodec.OnUpdateRSMatrixFormingProgress = OnUpdateRSMatrixFormingProgress;
            _eFileCodec.OnRSMatrixFormingFinish = OnRSMatrixFormingFinish;
            _eFileCodec.OnUpdateFileCodingProgress = OnUpdateFileCodingProgress;
            _eFileCodec.OnFileCodingFinish = OnFileCodingFinish;
      
            _eFileAnalyzer.OnUpdateFileAnalyzeProgress = OnUpdateFileAnalyzeProgress;
            _eFileAnalyzer.OnFileAnalyzeFinish = OnFileAnalyzeFinish;
                                 
            // ���������, ��� ����� ������ �����������
            _exitEvent[0].Reset();
            _executeEvent[0].Set();
            _finishedEvent[0].Reset();

            // ���� �������, ��� �� ��������� ������ � ��������� ������,
            // ��������� � ������
            if (!runAsSeparateThread)
            {
                // �������� ���� �� ����������� (�������� ���)
                Protect();
                
                // ���������� ��������� ���������
                return _processedOK;
            }
           
            // ������� ����� ����������� ������...
            _thrFileCodingProc = new Thread(new ThreadStart(Protect));

            //...����� ���� ��� ���...
            _thrFileCodingProc.Name = "FileCodec.Protect()";

            //...������������� ��������� ��������� ������...
            _thrFileCodingProc.Priority = _threadPriority;

            //...� ��������� ���
            _thrFileCodingProc.Start();

            // ��������, ��� ��� ���������
            return true;
        }

        /// <summary>
        /// ���������������� ������������� �����
        /// </summary>        
        /// <param name="fullFilename">������ ��� ����� ��� ��������������</param>
        /// <param name="runAsSeparateThread">��������� � ��������� ������?</param>
        /// <returns>��������� ���� ��������</returns>
        public bool StartToRecover(String fullFilename, bool runAsSeparateThread)
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
                        
            // ���� ��� ����� �� �����������
            if (
                    (fullFilename == null)
                 ||
                    (fullFilename == "")
                )
            {
                // ���������� ��������� ����������� ��������� ����������-������
                _finished = true;

                // ������������� ������� ���������� ���������
                _finishedEvent[0].Set();

                return false;
            }

            // ���������� ��������� ���� �� ������� ����� �����
            _path = _eFileNamer.GetPath(fullFilename);

            // ���������� ��������� ����� �� ������� ����� �����
            _filename = _eFileNamer.GetShortFilename(fullFilename);

            // ���� �������� ���� �� ����������, �������� �� ������
            if (!File.Exists(_path + _filename))
            {
                // ���������� ��������� ����������� ��������� ����������-������
                _finished = true;

                // ������������� ������� ���������� ���������
                _finishedEvent[0].Set();

                return false;
            }

            // ������������� �������� ��� ����� �� ����������� �������,
            // � � ���������� �������� ��������� "_filename", "_dataCount", "_eccCount"
            if (!_eFileNamer.Unpack(ref _filename, ref _dataCount, ref _eccCount))
            {
                return false;
            }
           
            // ������������� �� ���������
            _eFileSplitter.OnUpdateFileSplittingProgress = OnUpdateFileSplittingProgress;
            _eFileSplitter.OnFileSplittingFinish = OnFileSplittingFinish;

            _eFileCodec.OnUpdateRSMatrixFormingProgress = OnUpdateRSMatrixFormingProgress;
            _eFileCodec.OnRSMatrixFormingFinish = OnRSMatrixFormingFinish;
            _eFileCodec.OnUpdateFileCodingProgress = OnUpdateFileCodingProgress;
            _eFileCodec.OnFileCodingFinish = OnFileCodingFinish;

            _eFileAnalyzer.OnUpdateFileAnalyzeProgress = OnUpdateFileAnalyzeProgress;
            _eFileAnalyzer.OnFileAnalyzeFinish = OnFileAnalyzeFinish;
            _eFileAnalyzer.OnGetDamageStat = OnGetDamageStat;
           
            // ���������, ��� ����� ������ �����������
            _exitEvent[0].Reset();
            _executeEvent[0].Set();
            _finishedEvent[0].Reset();

            // ���� �������, ��� �� ��������� ������ � ��������� ������,
            // ��������� � ������
            if (!runAsSeparateThread)
            {
                // ��������������� ���� �� ������������ ������ � ���������� ������
                Recover();
                
                // ���������� ��������� ���������
                return _processedOK;
            }
            
            // ������� ����� �������������� ������...
            _thrFileCodingProc = new Thread(new ThreadStart(Recover));

            //...����� ���� ��� ���...
            _thrFileCodingProc.Name = "FileCodec.Recover()";

            //...������������� ��������� ��������� ������...
            _thrFileCodingProc.Priority = _threadPriority;

            //...� ��������� ���
            _thrFileCodingProc.Start();

            // ��������, ��� ��� ���������
            return true;
        }

        /// <summary>
        /// �������������� ����������������� ������ ������
        /// </summary>
        /// <param name="fullFilename">������ ��� ����� ��� ��������������</param>
        /// <param name="runAsSeparateThread">��������� � ��������� ������?</param>
        /// <returns>��������� ���� ��������</returns>
        public bool StartToRepair(String fullFilename, bool runAsSeparateThread)
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

            // ���� ��� ����� �� �����������
            if (
                    (fullFilename == null)
                 ||
                    (fullFilename == "")
                )
            {
                // ���������� ��������� ����������� ��������� ����������-������
                _finished = true;

                // ������������� ������� ���������� ���������
                _finishedEvent[0].Set();

                return false;
            }

            // ���������� ��������� ���� �� ������� ����� �����
            _path = _eFileNamer.GetPath(fullFilename);

            // ���������� ��������� ����� �� ������� ����� �����
            _filename = _eFileNamer.GetShortFilename(fullFilename);

            // ���� �������� ���� �� ����������, �������� �� ������
            if (!File.Exists(_path + _filename))
            {
                // ���������� ��������� ����������� ��������� ����������-������
                _finished = true;

                // ������������� ������� ���������� ���������
                _finishedEvent[0].Set();

                return false;
            }

            // ������������� �������� ��� ����� �� ����������� �������,
            // � � ���������� �������� ��������� "_filename", "_dataCount", "_eccCount"
            if (!_eFileNamer.Unpack(ref _filename, ref _dataCount, ref _eccCount))
            {
                return false;
            }

            // ������������� �� ���������
            _eFileCodec.OnUpdateRSMatrixFormingProgress = OnUpdateRSMatrixFormingProgress;
            _eFileCodec.OnRSMatrixFormingFinish = OnRSMatrixFormingFinish;
            _eFileCodec.OnUpdateFileCodingProgress = OnUpdateFileCodingProgress;
            _eFileCodec.OnFileCodingFinish = OnFileCodingFinish;

            _eFileAnalyzer.OnUpdateFileAnalyzeProgress = OnUpdateFileAnalyzeProgress;
            _eFileAnalyzer.OnFileAnalyzeFinish = OnFileAnalyzeFinish;
            _eFileAnalyzer.OnGetDamageStat = OnGetDamageStat;

            // ���������, ��� ����� ������ �����������
            _exitEvent[0].Reset();
            _executeEvent[0].Set();
            _finishedEvent[0].Reset();

            // ���� �������, ��� �� ��������� ������ � ��������� ������,
            // ��������� � ������
            if (!runAsSeparateThread)
            {
                // ��������������� ���� �� ������������ ������ � ���������� ������
                Repair();

                // ���������� ��������� ���������
                return _processedOK;
            }

            // ������� ����� �������������� ������...
            _thrFileCodingProc = new Thread(new ThreadStart(Repair));

            //...����� ���� ��� ���...
            _thrFileCodingProc.Name = "FileCodec.Repair()";

            //...������������� ��������� ��������� ������...
            _thrFileCodingProc.Priority = _threadPriority;

            //...� ��������� ���
            _thrFileCodingProc.Start();

            // ��������, ��� ��� ���������
            return true;
        }

        /// <summary>
        /// ������������ ����������������� ������ ������
        /// </summary>
        /// <param name="fullFilename">������ ��� ����� ��� ������������</param>
        /// <param name="runAsSeparateThread">��������� � ��������� ������?</param>
        /// <returns>��������� ���� ��������</returns>
        public bool StartToTest(String fullFilename, bool runAsSeparateThread)
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

            // ���� ��� ����� �� �����������
            if (
                    (fullFilename == null)
                 ||
                    (fullFilename == "")
                )
            {
                // ���������� ��������� ����������� ��������� ����������-������
                _finished = true;

                // ������������� ������� ���������� ���������
                _finishedEvent[0].Set();

                return false;
            }

            // ���������� ��������� ���� �� ������� ����� �����
            _path = _eFileNamer.GetPath(fullFilename);

            // ���������� ��������� ����� �� ������� ����� �����
            _filename = _eFileNamer.GetShortFilename(fullFilename);

            // ���� �������� ���� �� ����������, �������� �� ������
            if (!File.Exists(_path + _filename))
            {
                // ���������� ��������� ����������� ��������� ����������-������
                _finished = true;

                // ������������� ������� ���������� ���������
                _finishedEvent[0].Set();

                return false;
            }

            // ������������� �������� ��� ����� �� ����������� �������,
            // � � ���������� �������� ��������� "_filename", "_dataCount", "_eccCount"
            if (!_eFileNamer.Unpack(ref _filename, ref _dataCount, ref _eccCount))
            {
                return false;
            }

            // ������������� �� ���������          
            _eFileAnalyzer.OnUpdateFileAnalyzeProgress = OnUpdateFileAnalyzeProgress;
            _eFileAnalyzer.OnFileAnalyzeFinish = OnFileAnalyzeFinish;
            _eFileAnalyzer.OnGetDamageStat = OnGetDamageStat;

            // ���������, ��� ����� ������ �����������
            _exitEvent[0].Reset();
            _executeEvent[0].Set();
            _finishedEvent[0].Reset();

            // ���� �������, ��� �� ��������� ������ � ��������� ������,
            // ��������� � ������
            if (!runAsSeparateThread)
            {
                // ��������������� ���� �� ������������ ������ � ���������� ������
                Test();

                // ���������� ��������� ���������
                return _processedOK;
            }

            // ������� ����� �������������� ������...
            _thrFileCodingProc = new Thread(new ThreadStart(Test));

            //...����� ���� ��� ���...
            _thrFileCodingProc.Name = "FileCodec.Test()";

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
        /// ���������������� ����������� ����� �� ���� RAID
        /// </summary>
        private void Protect()
        {                      
            // ��������� �������� ���� �� ���������
            if (_eFileSplitter.StartToSplit(_path, _filename, _dataCount, _eccCount, true))
            {
                // ���� �������� ���������� ����� ��������� ��������� ����� �� ����              
                while (true)
                {
                    // ���� �� ���������� �������������� ������� "_executeEvent",
                    // �� ������������ �����, ����� �� ��������� ��������� �� ����� -                       
                    if (!ManualResetEvent.WaitAll(_executeEvent, 0, false))
                    {
                        //...���������������� ������ ��������������� ���������...                      
                        _eFileSplitter.Pause();

                        //...� ���� ��������
                        ManualResetEvent.WaitAll(_executeEvent);

                        // � ����� ����������, ���������, ��� ��������� ������ ������������                            
                        _eFileSplitter.Continue();
                    }

                    // ���� ����� �� ������������� �������...
                    int eventIdx = ManualResetEvent.WaitAny(new ManualResetEvent[] { _wakeUpEvent[0], _exitEvent[0], _eFileSplitter.FinishedEvent[0] });

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
                        ///...������������� �������������� ��������
                        _eFileSplitter.Stop();

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
                if (!_eFileSplitter.Finished)
                {
                    Thread.Sleep(100);
                }
            }

            // ���� 15 ����� �������� �������� �������� ������� �� ������� � ���������
            // ���������� - ��� ������
            if (!_eFileSplitter.ProcessedOK)
            {
                // ��������� �� ��, ��� ��������� ���� ��������
                _processedOK = false;

                // ���������� ��������� ����������� ��������� ����������-������
                _finished = true;

                // ������������� ������� ���������� ���������
                _finishedEvent[0].Set();

                return;
            }

            // ������� ���� ��� ��������������
            if (_eFileCodec.StartToEncode(_path, _filename, _dataCount, _eccCount, true))
            {
                // ���� �������� ���������� ����� ����������� �����          
                while (true)
                {
                    // ���� �� ���������� �������������� ������� "_executeEvent",
                    // �� ������������ �����, ����� �� ��������� ��������� �� ����� -                       
                    if (!ManualResetEvent.WaitAll(_executeEvent, 0, false))
                    {
                        //...���������������� ������ ��������������� ���������...                      
                        _eFileCodec.Pause();

                        //...� ���� ��������
                        ManualResetEvent.WaitAll(_executeEvent);

                        // � ����� ����������, ���������, ��� ��������� ������ ������������                            
                        _eFileCodec.Continue();
                    }

                    // ���� ����� �� ������������� �������...
                    int eventIdx = ManualResetEvent.WaitAny(new ManualResetEvent[] { _wakeUpEvent[0], _exitEvent[0], _eFileCodec.FinishedEvent[0] });

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
                        ///...������������� �������������� ��������
                        _eFileCodec.Stop();

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
                if (!_eFileCodec.Finished)
                {
                    Thread.Sleep(100);
                }
            }

            // ���� 15 ����� �������� �������� �������� ������� �� ������� � ���������
            // ���������� - ��� ������
            if (!_eFileCodec.ProcessedOK)
            {
                // ��������� �� ��, ��� ��������� ���� ��������
                _processedOK = false;

                // ���������� ��������� ����������� ��������� ����������-������
                _finished = true;

                // ������������� ������� ���������� ���������
                _finishedEvent[0].Set();

                return;
            }

            // ������������ ���������� �������� ����������� CRC-64 ��� ����� ������ �����
            if (_eFileAnalyzer.StartToWriteCRC64(_path, _filename, _dataCount, _eccCount, true))
            {
                // ���� �������� ���������� �������� ������� �������� ����������� �����                
                while (true)
                {
                    // ���� �� ���������� �������������� ������� "_executeEvent",
                    // �� ������������ �����, ����� �� ��������� ��������� �� ����� -                       
                    if (!ManualResetEvent.WaitAll(_executeEvent, 0, false))
                    {
                        //...���������������� ������ ��������������� ���������...                      
                        _eFileAnalyzer.Pause();

                        //...� ���� ��������
                        ManualResetEvent.WaitAll(_executeEvent);
                        
                        // � ����� ����������, ���������, ��� ��������� ������ ������������                            
                        _eFileAnalyzer.Continue();
                    }

                    // ���� ����� �� ������������� �������...
                    int eventIdx = ManualResetEvent.WaitAny(new ManualResetEvent[] { _wakeUpEvent[0], _exitEvent[0], _eFileAnalyzer.FinishedEvent[0] });

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
                        ///...������������� �������������� ��������
                        _eFileAnalyzer.Stop();

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
                if (!_eFileAnalyzer.Finished)
                {
                    Thread.Sleep(100);
                }
            }

            // ���� 15 ����� �������� �������� �������� ������� �� ������� � ���������
            // ���������� - ��� ������
            if (!_eFileAnalyzer.ProcessedOK)
            {
                // ��������� �� ��, ��� ��������� ���� ��������
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
        private void Recover()
        {
            // ������ �����, ������������ ��� ��������������
            int[] volList;
            
            // ������������ �������� �������� ����������� CRC-64 ��� ����� ������ �����
            if (_eFileAnalyzer.StartToAnalyzeCRC64(_path, _filename, _dataCount, _eccCount, true))
            {
                // ���� �������� ���������� �������� ������� �������� ����������� �����
                while (true)
                {
                    // ���� �� ���������� �������������� ������� "_executeEvent",
                    // �� ������������ �����, ����� �� ��������� ��������� �� ����� -                       
                    if (!ManualResetEvent.WaitAll(_executeEvent, 0, false))
                    {
                        //...���������������� ������ ��������������� ���������...                      
                        _eFileAnalyzer.Pause();

                        //...� ���� ��������
                        ManualResetEvent.WaitAll(_executeEvent);

                        // � ����� ����������, ���������, ��� ��������� ������ ������������                            
                        _eFileAnalyzer.Continue();
                    }

                    // ���� ����� �� ������������� �������...
                    int eventIdx = ManualResetEvent.WaitAny(new ManualResetEvent[] { _wakeUpEvent[0], _exitEvent[0], _eFileAnalyzer.FinishedEvent[0] });

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
                        ///...������������� �������������� ��������
                        _eFileAnalyzer.Stop();

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
                if (!_eFileAnalyzer.Finished)
                {
                    Thread.Sleep(100);
                }
            }

            // ���� 15 ����� �������� �������� �������� ������� �� ������� � ���������
            // ���������� - ��� ������
            if (!_eFileAnalyzer.ProcessedOK)
            {
                // ��������� �� ��, ��� ��������� ���� ��������
                _processedOK = false;

                // ���������� ��������� ����������� ��������� ����������-������
                _finished = true;

                // ������������� ������� ���������� ���������
                _finishedEvent[0].Set();

                return;
            }

            // ������, ����� ��������� ���������, ���������� ����������������
            // ���������� ������ "volList"
            volList = _eFileAnalyzer.VolList;
                     
            // ���������� ������������, ��� �������������� ������ �� �����������
            bool needToRecover = false;

            // ��������� ������ �� ������� � ��� ����� ��� ��������������
            for (int dataNum = 0; dataNum < _dataCount; dataNum++)
            {                
                // ���� ���������� ��� ��� ��������������, �����
                // �������� ����� ���������� � ��������� ���������� "FileCodec"
                if (volList[dataNum] != dataNum)
                {
                    needToRecover = true;

                    break;
                }
            }

            // ���� ��������� �������������� �������� �����, ��������� ���
            if (needToRecover)
            {
                // ��������������� ��������� �������� ����
                if (_eFileCodec.StartToDecode(_path, _filename, _dataCount, _eccCount, volList, true))
                {                    
                    // ���� �������� ���������� ����� ������������� �����          
                    while (true)
                    {
                        // ���� �� ���������� �������������� ������� "_executeEvent",
                        // �� ������������ �����, ����� �� ��������� ��������� �� ����� -                       
                        if (!ManualResetEvent.WaitAll(_executeEvent, 0, false))
                        {
                            //...���������������� ������ ��������������� ���������...                      
                            _eFileCodec.Pause();

                            //...� ���� ��������
                            ManualResetEvent.WaitAll(_executeEvent);

                            // � ����� ����������, ���������, ��� ��������� ������ ������������                            
                            _eFileCodec.Continue();
                        }

                        // ���� ����� �� ������������� �������...
                        int eventIdx = ManualResetEvent.WaitAny(new ManualResetEvent[] { _wakeUpEvent[0], _exitEvent[0], _eFileCodec.FinishedEvent[0] });

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
                            ///...������������� �������������� ��������
                            _eFileCodec.Stop();

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
                    if (!_eFileCodec.Finished)
                    {
                        Thread.Sleep(100);
                    }
                }

                // ���� 15 ����� �������� �������� �������� ������� �� ������� � ���������
                // ���������� - ��� ������
                if (!_eFileCodec.ProcessedOK)
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
                        
            // ��������� �������� ���� �� ��������������� �������� �����
            if (_eFileSplitter.StartToGlue(_path, _filename, _dataCount, _eccCount, true))
            {
                // ���� �������� ���������� ����� ���������� ��������� ����� �� �����                
                while (true)
                {
                    // ���� �� ���������� �������������� ������� "_executeEvent",
                    // �� ������������ �����, ����� �� ��������� ��������� �� ����� -                       
                    if (!ManualResetEvent.WaitAll(_executeEvent, 0, false))
                    {
                        //...���������������� ������ ��������������� ���������...                      
                        _eFileSplitter.Pause();

                        //...� ���� ��������
                        ManualResetEvent.WaitAll(_executeEvent);

                        // � ����� ����������, ���������, ��� ��������� ������ ������������                            
                        _eFileSplitter.Continue();
                    }

                    // ���� ����� �� ������������� �������...
                    int eventIdx = ManualResetEvent.WaitAny(new ManualResetEvent[] { _wakeUpEvent[0], _exitEvent[0], _eFileSplitter.FinishedEvent[0] });

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
                        ///...������������� �������������� ��������
                        _eFileSplitter.Stop();

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
                if (!_eFileSplitter.Finished)
                {
                    Thread.Sleep(100);
                }
            }

            // ���� 15 ����� �������� �������� �������� ������� �� ������� � ���������
            // ���������� - ��� ������
            if (!_eFileSplitter.ProcessedOK)
            {
                // ��������� �� ��, ��� ��������� ���� ��������
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
        /// "�������" ������ ������
        /// </summary>
        private void Repair()
        {
            // ������ �����, ������������ ��� ��������������
            int[] volList;

            // ������������ �������� �������� ����������� CRC-64 ��� ����� ������ �����
            if (_eFileAnalyzer.StartToAnalyzeCRC64(_path, _filename, _dataCount, _eccCount, true))
            {
                // ���� �������� ���������� �������� ������� �������� ����������� �����
                while (true)
                {
                    // ���� �� ���������� �������������� ������� "_executeEvent",
                    // �� ������������ �����, ����� �� ��������� ��������� �� ����� -                       
                    if (!ManualResetEvent.WaitAll(_executeEvent, 0, false))
                    {
                        //...���������������� ������ ��������������� ���������...                      
                        _eFileAnalyzer.Pause();

                        //...� ���� ��������
                        ManualResetEvent.WaitAll(_executeEvent);

                        // � ����� ����������, ���������, ��� ��������� ������ ������������                            
                        _eFileAnalyzer.Continue();
                    }

                    // ���� ����� �� ������������� �������...
                    int eventIdx = ManualResetEvent.WaitAny(new ManualResetEvent[] { _wakeUpEvent[0], _exitEvent[0], _eFileAnalyzer.FinishedEvent[0] });

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
                        ///...������������� �������������� ��������
                        _eFileAnalyzer.Stop();

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
                if (!_eFileAnalyzer.Finished)
                {
                    Thread.Sleep(100);
                }
            }

            // ���� 15 ����� �������� �������� �������� ������� �� ������� � ���������
            // ���������� - ��� ������
            if (!_eFileAnalyzer.ProcessedOK)
            {
                // ��������� �� ��, ��� ��������� ���� ��������
                _processedOK = false;

                // ���������� ��������� ����������� ��������� ����������-������
                _finished = true;

                // ������������� ������� ���������� ���������
                _finishedEvent[0].Set();

                return;
            }

            // ������, ����� ��������� ���������, ���������� ����������������
            // ���������� ������ "volList"
            volList = _eFileAnalyzer.VolList;

            // ���������� ������������, ��� �������������� ������ �� �����������
            bool needToRecover = false;

            // ��������� ������ �� ������� � ��� ����� ��� ��������������
            for (int dataNum = 0; dataNum < _dataCount; dataNum++)
            {
                // ���� ���������� ��� ��� ��������������, �����
                // �������� ����� ���������� � ��������� ���������� "FileCodec"
                if (volList[dataNum] != dataNum)
                {
                    needToRecover = true;

                    break;
                }
            }

            // ���� ��������� �������������� �������� �����, ��������� ���
            if (needToRecover)
            {
                // ��������������� ��������� �������� ����
                if (_eFileCodec.StartToDecode(_path, _filename, _dataCount, _eccCount, volList, true))
                {
                    // ���� �������� ���������� ����� ������������� �����          
                    while (true)
                    {
                        // ���� �� ���������� �������������� ������� "_executeEvent",
                        // �� ������������ �����, ����� �� ��������� ��������� �� ����� -                       
                        if (!ManualResetEvent.WaitAll(_executeEvent, 0, false))
                        {
                            //...���������������� ������ ��������������� ���������...                      
                            _eFileCodec.Pause();

                            //...� ���� ��������
                            ManualResetEvent.WaitAll(_executeEvent);

                            // � ����� ����������, ���������, ��� ��������� ������ ������������                            
                            _eFileCodec.Continue();
                        }

                        // ���� ����� �� ������������� �������...
                        int eventIdx = ManualResetEvent.WaitAny(new ManualResetEvent[] { _wakeUpEvent[0], _exitEvent[0], _eFileCodec.FinishedEvent[0] });

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
                            ///...������������� �������������� ��������
                            _eFileCodec.Stop();

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
                    if (!_eFileCodec.Finished)
                    {
                        Thread.Sleep(100);
                    }
                }

                // ���� 15 ����� �������� �������� �������� ������� �� ������� � ���������
                // ���������� - ��� ������
                if (!_eFileCodec.ProcessedOK)
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
          
            // �������� ����� (��������� ��� ����, ����� ��������� ������ �� ������ ������)            
            FileStream eFileStream = null;

            try
            {
                // ��� ����� ��� ���������
                String filename;

                // ������������ ��� �����
                for (int i = 0; i < (_dataCount + _eccCount); i++)
                {
                    // ��������� �������������� ��� �����,...
                    filename = _filename;

                    //...����������� ��� � ���������� ������...
                    _eFileNamer.Pack(ref filename, i, _dataCount, _eccCount);

                    //...��������� ������ ��� �����...
                    filename = _path + filename;

                    //...���������� ���� �� ������� �����...                    
                    if (File.Exists(filename))
                    {
                        //...���� ������� �������, ������ �� ���� ��������
                        // ��-���������
                        File.SetAttributes(filename, FileAttributes.Normal);

                        //...��������� �������� ����� �� ������...
                        eFileStream = new FileStream(filename, FileMode.Open, System.IO.FileAccess.Write);

                        if (eFileStream != null)
                        {
                            //...����������� ��� ����� �� 8 ���� (������ CRC-64)...
                            eFileStream.SetLength(eFileStream.Length - 8);

                            //...������� �������� �����...
                            eFileStream.Flush();

                            //...� ��������� ����
                            eFileStream.Close();

                            // ���� ������� ����� - ����������� ��� null, ����� � ������
                            // �������������� �������� ��������� ������������ ���������� ������                        
                            eFileStream = null;
                        }
                    }
                }             
            }

            // ���� ���� ���� �� ���� ���������� - ��������� �������� ����� �
            // �������� �� ������
            catch (IOException e)
            {
                // ��������� �������� �����
                if (eFileStream != null)
                {
                    eFileStream.Close();
                    eFileStream = null;
                }

                // ��������� �� ��, ��� ������� "�������" ������ ������ ������ �����������
                _processedOK = false;

                // ������������� ��������� ����������� ��������� ����������-������
                _finished = true;

                return;
            }

            // ���� � ���������� ������� ������ ����� ���� �����������, ���
            // ��� ���� ��� �������������� �������� ���������������,
            // ��� ����������� � �� ��������� ��������
            if (!_eFileAnalyzer.AllEccVolsOK)
            {
                // ������� ���� ��� ��������������
                if (_eFileCodec.StartToEncode(_path, _filename, _dataCount, _eccCount, true))
                {
                    // ���� �������� ���������� ����� ����������� �����          
                    while (true)
                    {
                        // ���� �� ���������� �������������� ������� "_executeEvent",
                        // �� ������������ �����, ����� �� ��������� ��������� �� ����� -                       
                        if (!ManualResetEvent.WaitAll(_executeEvent, 0, false))
                        {
                            //...���������������� ������ ��������������� ���������...                      
                            _eFileCodec.Pause();

                            //...� ���� ��������
                            ManualResetEvent.WaitAll(_executeEvent);

                            // � ����� ����������, ���������, ��� ��������� ������ ������������                            
                            _eFileCodec.Continue();
                        }

                        // ���� ����� �� ������������� �������...
                        int eventIdx = ManualResetEvent.WaitAny(new ManualResetEvent[] { _wakeUpEvent[0], _exitEvent[0], _eFileCodec.FinishedEvent[0] });

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
                            ///...������������� �������������� ��������
                            _eFileCodec.Stop();

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
                    if (!_eFileCodec.Finished)
                    {
                        Thread.Sleep(100);
                    }
                }

                // ���� 15 ����� �������� �������� �������� ������� �� ������� � ���������
                // ���������� - ��� ������
                if (!_eFileCodec.ProcessedOK)
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

            // ������������ ���������� �������� ����������� CRC-64 ��� ����� ������ �����
            if (_eFileAnalyzer.StartToWriteCRC64(_path, _filename, _dataCount, _eccCount, true))
            {
                // ���� �������� ���������� �������� ������� �������� ����������� �����                
                while (true)
                {
                    // ���� �� ���������� �������������� ������� "_executeEvent",
                    // �� ������������ �����, ����� �� ��������� ��������� �� ����� -                       
                    if (!ManualResetEvent.WaitAll(_executeEvent, 0, false))
                    {
                        //...���������������� ������ ��������������� ���������...                      
                        _eFileAnalyzer.Pause();

                        //...� ���� ��������
                        ManualResetEvent.WaitAll(_executeEvent);

                        // � ����� ����������, ���������, ��� ��������� ������ ������������                            
                        _eFileAnalyzer.Continue();
                    }

                    // ���� ����� �� ������������� �������...
                    int eventIdx = ManualResetEvent.WaitAny(new ManualResetEvent[] { _wakeUpEvent[0], _exitEvent[0], _eFileAnalyzer.FinishedEvent[0] });

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
                        ///...������������� �������������� ��������
                        _eFileAnalyzer.Stop();

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
                if (!_eFileAnalyzer.Finished)
                {
                    Thread.Sleep(100);
                }
            }

            // ���� 15 ����� �������� �������� �������� ������� �� ������� � ���������
            // ���������� - ��� ������
            if (!_eFileAnalyzer.ProcessedOK)
            {
                // ��������� �� ��, ��� ��������� ���� ��������
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
        /// ������������ ������ ������
        /// </summary>
        private void Test()
        {
            // ������������ �������� �������� ����������� CRC-64 ��� ����� ������ �����
            if (_eFileAnalyzer.StartToAnalyzeCRC64(_path, _filename, _dataCount, _eccCount, true))
            {
                // ���� �������� ���������� �������� ������� �������� ����������� �����
                while (true)
                {
                    // ���� �� ���������� �������������� ������� "_executeEvent",
                    // �� ������������ �����, ����� �� ��������� ��������� �� ����� -                       
                    if (!ManualResetEvent.WaitAll(_executeEvent, 0, false))
                    {
                        //...���������������� ������ ��������������� ���������...                      
                        _eFileAnalyzer.Pause();

                        //...� ���� ��������
                        ManualResetEvent.WaitAll(_executeEvent);

                        // � ����� ����������, ���������, ��� ��������� ������ ������������                            
                        _eFileAnalyzer.Continue();
                    }

                    // ���� ����� �� ������������� �������...
                    int eventIdx = ManualResetEvent.WaitAny(new ManualResetEvent[] { _wakeUpEvent[0], _exitEvent[0], _eFileAnalyzer.FinishedEvent[0] });

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
                        ///...������������� �������������� ��������
                        _eFileAnalyzer.Stop();

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
                if (!_eFileAnalyzer.Finished)
                {
                    Thread.Sleep(100);
                }
            }

            // ���� 15 ����� �������� �������� �������� ������� �� ������� � ���������
            // ���������� - ��� ������
            if (!_eFileAnalyzer.ProcessedOK)
            {
                // ��������� �� ��, ��� ��������� ���� ��������
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

                    if (_eFileAnalyzer != null)
                    {
                        _eFileAnalyzer.ThreadPriority = value;
                    }

                    if (_eFileCodec != null)
                    {
                        _eFileCodec.ThreadPriority = value;
                    }

                    if (_eFileSplitter != null)
                    {
                        _eFileSplitter.ThreadPriority = value;
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

        // ������ ���������� � �������� ��������� ����������� ����� CRC-64
        private FileAnalyzer _eFileAnalyzer;
        
        // RAID-�������� �������� �����
        private FileCodec _eFileCodec;
       
        // ������ ��������� (����������) ������ �� ����
        private FileSplitter _eFileSplitter;

        // ���������� �������� �����
        private int _dataCount;

        // ���������� ����� ��� ��������������
        private int _eccCount;

        // ���� � ������ ��� ���������
        private String _path;

        // ��� ��������� ����� ��� ���������
        private String _filename;

        // �������� ������ ��������� �������� ���������?
        private bool _finished;

        // ��������� ����������� ���������?
        private bool _processedOK;
        
        // ����� ����������� ������
        private Thread _thrFileCodingProc;

        // ��������� �������� ��������� (����������) �����
        private ThreadPriority _threadPriority;

        // ������� ����������� ��������� �����
        private ManualResetEvent[] _exitEvent;

        // ������� ����������� ��������� �����
        private ManualResetEvent[] _executeEvent;

        // ������� "�����������" ����� ��������
        private ManualResetEvent[] _wakeUpEvent;

        // �������, ��������������� �� ���������� ���������
        private ManualResetEvent[] _finishedEvent;

        #endregion Data

    } // class RecoveryStarCore
}
