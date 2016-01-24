/*----------------------------------------------------------------------+
 |	filename:	FileSplitter.cs                                         |
 |----------------------------------------------------------------------|
 |	version:	1.14                                                    |
 |	revision:   02/03/2007	08:45                                       |
 |  author:		�������� ���� ��������� (DrAF)                         |
 |	e-mail:		draf@mail.ru                                            |
 |	purpose:	������ � �������                                        |
 +----------------------------------------------------------------------+
 |  ���������� (����������) ������-����� �� ���������                   |
 +----------------------------------------------------------------------*/

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.IO;

namespace RecoveryStar
{
    /// <summary>
    /// ������� ���������� ��������� ���������� (����������) �����
    /// </summary>
    /// <param name="progress">������� ��������</param>
    public delegate void OnUpdateFileSplittingProgressHandler(double progress);

    /// <summary>
    /// ������� ���������� �������� ���������� (����������) �����
    /// </summary>
    public delegate void OnFileSplittingFinishHandler();

    /// <summary>
    /// ����� ��� ���������� (����������) ������ �� ���������
    /// </summary>
    public class FileSplitter
    {
        #region Construction & Destruction

        public FileSplitter()
        {            
            // ������� ��������� ������ ��� ������������ ����� ����            
            _eFileNamer = new FileNamer();

            // ���� � ������ ��� ��������� ��-��������� ������
            _path = "";

            // �������������� ��� ����� ��-���������            
            _filename = "NONAME";

            // ������ ��������� ������ (��-���������) - 64 ��
            _bufferLength = (64 * 1024 * 1024);

            // �������� ������ ��� �������� �����
            _buffer = new byte[_bufferLength];

            // � ���������� Int64 ���������� 8 ����
            _dataLengthArr = new byte[8];

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

        #endregion Delegates

        #region Public Operations
        
        /// <summary>
        /// ��������� ��������� ����� �� ��������� (����)
        /// </summary>
        /// <param name="path">���� � ������ ��� ���������</param>
        /// <param name="filename">��� ����� ��� ���������</param>        
        /// <param name="dataCount">������������ ���������� �������� �����</param>
        /// <param name="eccCount">������������ ���������� ����� ��� ��������������</param>
        /// <param name="runAsSeparateThread">��������� � ��������� ������?</param>
        /// <returns>��������� ���� ��������</returns>
        public bool StartToSplit(String path, String filename, int dataCount, int eccCount, bool runAsSeparateThread)
        {
            // ���� ����� ��������� ����� �� ��������� �������� - �� ��������� ��������� ������        
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

            // ���������, ��� ����� ������ �����������
            _exitEvent[0].Reset();
            _executeEvent[0].Set();            
            _finishedEvent[0].Reset();

            // ���� �������, ��� �� ��������� ������ � ��������� ������,
            // ��������� � ������
            if (!runAsSeparateThread)
            {
                // ��������� �������� ���� �� ���������
                Split();

                // ���������� ��������� ���������
                return _processedOK;
            }

            // ������� ����� ��������� ����� �� ���������...
            _thrFileVolProc = new Thread(new ThreadStart(Split));

            //...����� ���� ��� ���...
            _thrFileVolProc.Name = "FileSplitter.Split()";
      
            //...������������� ��������� ��������� ������...
            _thrFileVolProc.Priority = _threadPriority;
           
            //...� ��������� ���
            _thrFileVolProc.Start();

            // ��������, ��� ��� ���������
            return true;           
        }

        /// <summary>
        /// ���������� ����� �� ����������
        /// </summary>
        /// <param name="path">���� � ������ ��� ���������</param>
        /// <param name="filename">��� ����� ������ �� �������� �����</param>        
        /// <param name="dataCount">������������ ���������� �������� �����</param>
        /// <param name="eccCount">������������ ���������� ����� ��� ��������������</param>
        /// <param name="runAsSeparateThread">��������� � ��������� ������?</param>
        /// <returns>��������� ���� ��������</returns>
        public bool StartToGlue(String path, String filename, int dataCount, int eccCount, bool runAsSeparateThread)
        {
            // ���� ����� ���������� ����� �� ���������� �������� - �� ��������� ��������� ������
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
                // ��������� "����������" ������ �� ���������� � ��������
                Glue();

                // ���������� ��������� ���������
                return _processedOK;
            }

            // ������� ����� ���������� ������ �� ����������...
            _thrFileVolProc = new Thread(new ThreadStart(Glue));

            //...����� ���� ��� ���...
            _thrFileVolProc.Name = "FileSplitter.Glue()";
    
            //...������������� ��������� ��������� ������...
            _thrFileVolProc.Priority = _threadPriority;
            
            //...� ��������� ���
            _thrFileVolProc.Start();

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
        /// ��������� ����� �� ���������
        /// </summary>
        private void Split()
        {
            // ���������� �������� ������� (�������� � �������)
            FileStream fileStreamSource = null;
            FileStream fileStreamTarget = null;
            
            // ��������� ������ ������ ������� ����� ������ � �������� ����
            BinaryWriter eBinaryWriter = null;
            
            try
            {
                // ��� ����� ��� ���������
                String filename;

                // ��������� ������ ��� �����
                filename = _path + _filename;

                // ��������� ����� ��������� ����� �� ������
                fileStreamSource = new FileStream(filename, FileMode.Open, System.IO.FileAccess.Read);

                // ��������� ����� ��������� ����
                long volumeLength = (fileStreamSource.Length / _dataCount);

                // ��������� ������� �������������� ���� (�� ��������� ������ � �������)
                Int64 unwrittenCounter = fileStreamSource.Length;

                // ���� ��� ��������� ����� ���� ��� ������ �� ��������� � ����� �����,
                // ��������� ��� �� ������ ����� � ������� ����. ��� ���� � ����� ����� ����
                // ���� �� ����� ������������
                if ((fileStreamSource.Length % _dataCount) != 0)
                {
                    volumeLength++;
                }

                // ����� �� ���������� RAID-��������� ������ ����-�������� �������� ������ ������
                // �����. ������������ ���.
                if ((volumeLength % 2) != 0)
                {
                    volumeLength++;
                }
                                                
                // ��������� �������� ������, ������� �������� �������� ������� ���������
                // ����� ��� ��������� ���������� ��� ����� �� "volNum"
                int progressMod1 = (_dataCount / 100);

                // ���� ������ ����� ����, �� ����������� ��� �� �������� "1", �����
                // �������� ��������� �� ������ �������� (���� ����� ���������)
                if (progressMod1 == 0)
                {
                    progressMod1 = 1;
                }

                // ��������� �������� ������, ������� �������� �������� ������� ���������
                // ����� ��� ��������� ���������� ��� ����� �� "i" ������ ����� �� "volNum"
                int progressMod2 = (int)((fileStreamSource.Length / _bufferLength) / 100);

                // ���� ������ ����� ����, �� ����������� ��� �� �������� "1", �����
                // �������� ��������� �� ������ �������� (���� ����� ���������)
                if (progressMod2 == 0)
                {
                    progressMod2 = 1;
                }
                
                // C������ ���������� ���������� ���� � �������� �����
                Int64 volumeWriteCounter = 0;

                // �������� �� ����� ��������� ������ (+1 ��������� �������� ��� ������ �������)
                for (int volNum = 0; volNum <= _dataCount; volNum++)
                {
                    // ���� �� ��������� �� �� ������ ��������, �� ��������� ��������
                    // �������� ����� ��������� ������ � ��������� �������� �������
                    // ������������ ������
                    if (volNum != 0)
                    {
                        // ���������� ��������� �������� �������� ����� ��� �������������
                        // ���������� ������ ������ ������� ����� ������ � �������� ����
                        eBinaryWriter = new BinaryWriter(fileStreamTarget);

                        if (eBinaryWriter != null)
                        {
                            // ������������ �� ����� �����...
                            eBinaryWriter.Seek(0, SeekOrigin.End);

                            //...� ����� � ��� ����� ����� ����� �������� ������...                        
                            eBinaryWriter.Write(volumeWriteCounter);

                            //...�, �����, �������� �������� ��������
                            volumeWriteCounter = 0;

                            // ���������� ����� "BinaryWriter"
                            eBinaryWriter.Flush();

                            // ���������
                            eBinaryWriter.Close();
                            eBinaryWriter = null;
                        }
                        
                        if (fileStreamTarget != null)
                        {            
                            //...� ��������� �������� �����
                            fileStreamTarget.Close();
                            fileStreamTarget = null;
                        }
                    }

                    // ���� ������ ������� ���������� - �� ��������� �� ��������� ��������,
                    // � ��������� ����� �� ����� (�.�. ��� ���� ��� ����������)
                    if (volNum == _dataCount)
                    {
                        if (fileStreamSource != null)
                        {
                            // ����� ������� ��������� ����� ��������� �����
                            fileStreamSource.Close();
                            fileStreamSource = null;
                        }

                        // ��������, ��� ��������� ����� ���������
                        if (OnFileSplittingFinish != null)
                        {
                            OnFileSplittingFinish();
                        }

                        break;
                    }

                    // ��������� �������������� ��� �����
                    filename = _filename;
                    
                    // ����������� �������� ��� ����� � ���������� ������
                    // (��� �������� ���� ����� � �����)
                    if (!_eFileNamer.Pack(ref filename, volNum, _dataCount, _eccCount))
                    {
                        // ��������� �������� � ������� �������� ������
                        if (fileStreamSource != null)
                        {
                            fileStreamSource.Close();
                            fileStreamSource = null;
                        }
                        if (fileStreamTarget != null)
                        {
                            fileStreamTarget.Close();
                            fileStreamTarget = null;
                        }

                        // ��������� �� ��, ��� ��������� ������ ������ � �������
                        _processedOK = false;

                        // ���������� ��������� ����������� ��������� ����������-������
                        _finished = true;

                        // ������������� ������� ���������� ���������
                        _finishedEvent[0].Set();

                        return;
                    }
                   
                    // ��������� ������ ��� �����
                    filename = _path + filename;

                    // ��������� ����� �������� ����� �� ������
                    fileStreamTarget = new FileStream(filename, FileMode.Create, System.IO.FileAccess.Write);

                    // ���� ���� ��� ��������
                    if (unwrittenCounter > 0)
                    {
                        // ���������� �������� �������� (����������� ������ �������)
                        Int64 nIterations;

                        // �������, �� ����������� ��� ���������� �������� ��������
                        int iterRest;

                        // ���� ������� �������������� ���� ������ ���� ����� ������� ���� -
                        // ����� ��������� ������� ����������� � ����������� ��������� ��������
                        // ������� ���� � ��������� �� ����� ��������
                        if (unwrittenCounter >= volumeLength)
                        {
                            // ������ ���������� �������� �������� (����������� ������ �������)
                            nIterations = volumeLength / _bufferLength;

                            // ��������� �������, �� ����������� ��� ���������� �������� ��������
                            iterRest = (int)(volumeLength - (nIterations * _bufferLength));

                        } else
                        {
                            // ������ ���������� �������� �������� (����������� ������ �������)
                            nIterations = (unwrittenCounter / _bufferLength);

                            // ��������� �������, �� ����������� ��� ���������� �������� ��������
                            iterRest = (int)(unwrittenCounter - (nIterations * _bufferLength));

                            // ��������� ������ ��������� ������ �� "volumeLength"
                            fileStreamTarget.SetLength(volumeLength);
                        }

                        // ������ � ��������������� �������� (������ � �������� ���������)
                        for (Int64 i = 0; i < nIterations; i++)
                        {
                            // ������ ������ � �����
                            fileStreamSource.Read(_buffer, 0, _bufferLength);

                            // ����� ������ �� ������
                            fileStreamTarget.Write(_buffer, 0, _bufferLength);
                            volumeWriteCounter += _bufferLength;
                            unwrittenCounter   -= _bufferLength;

                            // ������� �������� ���������
                            if (
                                    ((((volNum * nIterations) + i) % progressMod2) == 0)
                                 &&
                                    (OnUpdateFileSplittingProgress != null)
                                )
                            {
                                OnUpdateFileSplittingProgress(((double)((volNum * nIterations) + (i + 1)) / (double)(_dataCount * nIterations)) * 100.0);
                            }

                            // � ������, ���� ��������� ���������� �� �����, ������� "_executeEvent"
                            // ����� ��������, � ����� �� ����� ������ �� ��� ���������
                            ManualResetEvent.WaitAll(_executeEvent);

                            // ���� �������, ��� ��������� ����� �� ������ - �������
                            if (ManualResetEvent.WaitAll(_exitEvent, 0, false))
                            {
                                // ��������� �������� � ������� �������� ������
                                if (fileStreamSource != null)
                                {
                                    fileStreamSource.Close();
                                    fileStreamSource = null;
                                }

                                if (fileStreamTarget != null)
                                {
                                    fileStreamTarget.Close();
                                    fileStreamTarget = null;
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

                        // ������������ ������� (���� �� ����)
                        if (iterRest > 0)
                        {
                            // ������ ������ � �����
                            fileStreamSource.Read(_buffer, 0, iterRest);

                            // ����� ������ �� ������
                            fileStreamTarget.Write(_buffer, 0, iterRest);
                            volumeWriteCounter += iterRest;
                            unwrittenCounter   -= iterRest;

                            // ������� �������� ���������
                            if (
                                    ((volNum % progressMod1) == 0)
                                 &&
                                    (OnUpdateFileSplittingProgress != null)
                                )
                            {
                                OnUpdateFileSplittingProgress(((double)(volNum + 1) / (double)_dataCount) * 100.0);
                            }

                            // � ������, ���� ��������� ���������� �� �����, ������� "_executeEvent"
                            // ����� ��������, � ����� �� ����� ������ �� ��� ���������
                            ManualResetEvent.WaitAll(_executeEvent);

                            // ���� �������, ��� ��������� ����� �� ������ - �������
                            if (ManualResetEvent.WaitAll(_exitEvent, 0, false))
                            {
                                // ��������� �������� � ������� �������� ������
                                if (fileStreamSource != null)
                                {
                                    fileStreamSource.Close();
                                    fileStreamSource = null;
                                }

                                if (fileStreamTarget != null)
                                {
                                    fileStreamTarget.Close();
                                    fileStreamTarget = null;
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

                        continue;
                    }

                    // ���� � �������� ������ ������ ������ ���, ����� ������ ���������
                    // ���� ��� ������, �� ����� ��� �������������� 8 ����� ����������,
                    // ����������� �� ���������� ������������ ������
                    if (unwrittenCounter == 0)
                    {
                        // ��������� ������ ��������� ������ �� "volumeLength"
                        fileStreamTarget.SetLength(volumeLength);

                        // ������� �������� ���������                     
                        if (
                                ((volNum % progressMod1) == 0)
                             &&
                                (OnUpdateFileSplittingProgress != null)
                            )
                        {
                            OnUpdateFileSplittingProgress(((double)(volNum + 1) / (double)_dataCount) * 100.0);
                        }

                        // � ������, ���� ��������� ���������� �� �����, ������� "_executeEvent"
                        // ����� ��������, � ����� �� ����� ������ �� ��� ���������
                        ManualResetEvent.WaitAll(_executeEvent);

                        // ���� �������, ��� ��������� ����� �� ������ - �������
                        if (ManualResetEvent.WaitAll(_exitEvent, 0, false))
                        {
                            // ��������� �������� � ������� �������� ������
                            if (fileStreamSource != null)
                            {
                                fileStreamSource.Close();
                                fileStreamSource = null;
                            }

                            if (fileStreamTarget != null)
                            {
                                fileStreamTarget.Close();
                                fileStreamTarget = null;
                            }

                            // ��������� �� ��, ��� ��������� ���� ��������
                            _processedOK = false;

                            // ���������� ��������� ����������� ��������� ����������-������
                            _finished = true;

                            // ������������� ������� ���������� ���������
                            _finishedEvent[0].Set();

                            return;
                        }

                        continue;
                    }
                }
            }

            // ���� ���� ���� �� ���� ���������� - ��������� �������� ������ �
            // �������� �� ������
            catch (IOException e)
            {
                // ��������� �������� � ������� �������� ������
                if (fileStreamSource != null)
                {
                    fileStreamSource.Close();
                    fileStreamSource = null;
                }

                if (fileStreamTarget != null)
                {
                    fileStreamTarget.Close();
                    fileStreamTarget = null;
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
        /// ���������� ������ �� ����������
        /// </summary>
        private void Glue()
        {
            // ���������� �������� ������� (�������� � �������)
            FileStream fileStreamSource = null;
            FileStream fileStreamTarget = null;

            // ����� �������� ����
            int volNum;

            // ��� ����� ��� ���������
            String filename;

            try
            {
                // ��������� ������ ��� �����
                filename = _path + _filename;

                // ��������� ����� �������� ����� �� ������
                fileStreamTarget = new FileStream(filename, FileMode.Create, System.IO.FileAccess.Write);
                
                // ��������� �������� ������, ������� �������� �������� ������� ���������
                // ����� ��� ��������� ���������� ��� ����� �� "volNum"
                int progressMod1 = (_dataCount / 100);

                // ���� ������ ����� ����, �� ����������� ��� �� �������� "1", �����
                // �������� ��������� �� ������ �������� (���� ����� ���������)
                if (progressMod1 == 0)
                {
                    progressMod1 = 1;
                }

                // ��������� �������������� ��� �����
                filename = _filename;

                // ����������� �������� ��� ����� � ���������� ������ ��� ��������� ����� ������� ����           
                _eFileNamer.Pack(ref filename, 0, _dataCount, _eccCount);

                // ��������� ������ ��� �����
                filename = _path + filename;

                // ��������� ����� ��������� ����� �� ������...
                fileStreamSource = new FileStream(filename, FileMode.Open, System.IO.FileAccess.Read);

                // ��������� �������� ������, ������� �������� �������� ������� ���������
                // ����� ��� ��������� ���������� ��� ����� �� "i" ������ ����� �� "volNum"
                int progressMod2 = (int)(((fileStreamSource.Length - 8) / _bufferLength) / 100);

                // ���� ������ ����� ����, �� ����������� ��� �� �������� "1", �����
                // �������� ��������� �� ������ �������� (���� ����� ���������)
                if (progressMod2 == 0)
                {
                    progressMod2 = 1;
                }

                // ��������� ����� ��������� �����
                if (fileStreamSource != null)
                {
                    fileStreamSource.Close();
                    fileStreamSource = null;
                }
                
                // �������� �� ����� ��������� ������
                for (volNum = 0; volNum < _dataCount; volNum++)
                {
                    // ��������� �������������� ��� �����
                    filename = _filename;

                    // ����������� �������� ��� ����� � ���������� ������
                    // (��� �������� ���� ����� � �����)
                    _eFileNamer.Pack(ref filename, volNum, _dataCount, _eccCount);
                    
                    // ��������� ������ ��� �����
                    filename = _path + filename;

                    // ���� �������� ���� �� ����������, �������� �� ������
                    if (!File.Exists(filename))
                    {
                        // ��������� �� ��, ��� ��������� ���� ��������
                        _processedOK = false;

                        // ���������� ��������� ����������� ��������� ����������-������
                        _finished = true;

                        // ������������� ������� ���������� ���������
                        _finishedEvent[0].Set();

                        return;
                    }

                    // ��������� ����� ��������� ����� �� ������...
                    fileStreamSource = new FileStream(filename, FileMode.Open, System.IO.FileAccess.Read);

                    //...� ��������� ���������������� �� ����� �����, ����� �������
                    // ���������� �������� ���� � ������ ����
                    fileStreamSource.Seek(((Int64)fileStreamSource.Length - (8 + 8)), SeekOrigin.Begin);

                    // ������ ����������� � ����� ����� �������� CRC-64...
                    fileStreamSource.Read(_dataLengthArr, 0, 8);

                    // ������������� ������ � ����� �� ������
                    fileStreamSource.Seek(0, SeekOrigin.Begin);

                    // ����������� � ����� �������� ���������� �������� ���� � ������ ����
                    UInt64 dataLength;

                    // ������ ����������� ������ byte[] � Int64
                    dataLength  = 0x0000000000000000;
                    dataLength |= ((UInt64)_dataLengthArr[0] << 0)  & (0x00000000000000FF);
                    dataLength |= ((UInt64)_dataLengthArr[1] << 8)  & (0x000000000000FF00);
                    dataLength |= ((UInt64)_dataLengthArr[2] << 16) & (0x0000000000FF0000);
                    dataLength |= ((UInt64)_dataLengthArr[3] << 24) & (0x00000000FF000000);
                    dataLength |= ((UInt64)_dataLengthArr[4] << 32) & (0x000000FF00000000);
                    dataLength |= ((UInt64)_dataLengthArr[5] << 40) & (0x0000FF0000000000);
                    dataLength |= ((UInt64)_dataLengthArr[6] << 48) & (0x00FF000000000000);
                    dataLength |= ((UInt64)_dataLengthArr[7] << 56) & (0xFF00000000000000);

                    // ������, ����� �� ����� ���������� �������� ���� � ������ ����, �� ��� �����
                    // �������� � ������� ����
                    // ������ ���������� �������� �������� (����������� ������ �������)
                    Int64 nIterations = (Int64)(dataLength / (UInt64)_bufferLength);

                    // ��������� �������, �� ����������� ��� ���������� �������� ��������
                    int iterRest = (int)((Int64)dataLength - (nIterations * _bufferLength));

                    // ������ � ��������������� �������� (������ � �������� ���������)
                    for (Int64 i = 0; i < nIterations; i++)
                    {
                        // ������ ������ � �����
                        fileStreamSource.Read(_buffer, 0, _bufferLength);

                        // ����� ������ �� ������
                        fileStreamTarget.Write(_buffer, 0, _bufferLength);

                        // ������� �������� ���������
                        if (
                                ((((volNum * nIterations) + i) % progressMod2) == 0)
                             &&
                                (OnUpdateFileSplittingProgress != null)
                            )
                        {
                            OnUpdateFileSplittingProgress(((double)((volNum * nIterations) + (i + 1)) / (double)(_dataCount * nIterations)) * 100.0);
                        }

                        // � ������, ���� ��������� ���������� �� �����, ������� "_executeEvent"
                        // ����� ��������, � ����� �� ����� ������ �� ��� ���������
                        ManualResetEvent.WaitAll(_executeEvent);

                        // ���� �������, ��� ��������� ����� �� ������ - �������
                        if (ManualResetEvent.WaitAll(_exitEvent, 0, false))
                        {
                            // ��������� �������� � ������� �������� ������
                            if (fileStreamSource != null)
                            {
                                fileStreamSource.Close();
                                fileStreamSource = null;
                            }

                            if (fileStreamTarget != null)
                            {
                                fileStreamTarget.Close();
                                fileStreamTarget = null;
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

                    // ������������ ������� (���� �� ����)
                    if (iterRest > 0)
                    {
                        // ������ ������ � �����
                        fileStreamSource.Read(_buffer, 0, iterRest);

                        // ����� ������ �� ������
                        fileStreamTarget.Write(_buffer, 0, iterRest);
            
                        // ������� �������� ���������                     
                        if (
                                ((volNum % progressMod1) == 0)
                             &&
                                (OnUpdateFileSplittingProgress != null)
                            )
                        {
                            OnUpdateFileSplittingProgress(((double)(volNum + 1) / (double)_dataCount) * 100.0);
                        }

                        // � ������, ���� ��������� ���������� �� �����, ������� "_executeEvent"
                        // ����� ��������, � ����� �� ����� ������ �� ��� ���������
                        ManualResetEvent.WaitAll(_executeEvent);

                        // ���� �������, ��� ��������� ����� �� ������ - �������
                        if (ManualResetEvent.WaitAll(_exitEvent, 0, false))
                        {
                            // ��������� �������� � ������� �������� ������
                            if (fileStreamSource != null)
                            {
                                fileStreamSource.Close();
                                fileStreamSource = null;
                            }

                            if (fileStreamTarget != null)
                            {
                                fileStreamTarget.Close();
                                fileStreamTarget = null;
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
                                        
                    // ��������� ���� ��������� ����                    
                    if (fileStreamSource != null)
                    {
                        fileStreamSource.Close();
                        fileStreamSource = null;
                    }
                }
                
                // ��������, ��� ��������� ����� ���������
                if (OnFileSplittingFinish != null)
                {
                    OnFileSplittingFinish();
                }
            }

            // ���� ���� ���� �� ���� ���������� - ��������� �������� ������ �
            // �������� �� ������
            catch (IOException e)
            {
                // ��������� �������� � ������� �������� ������
                if (fileStreamSource != null)
                {
                    fileStreamSource.Close();
                    fileStreamSource = null;
                }

                if (fileStreamTarget != null)
                {
                    fileStreamTarget.Close();
                    fileStreamTarget = null;
                }
               
                // ��������� �� ��, ��� ��������� ������ ������ � �������
                _processedOK = false;

                // ���������� ��������� ����������� ��������� ����������-������
                _finished = true;

                // ������������� ������� ���������� ���������
                _finishedEvent[0].Set();

                return;
            }

            // ��������� ������� ����                        
            if (fileStreamTarget != null)
            {
                fileStreamTarget.Flush();
                fileStreamTarget.Close();
                fileStreamTarget = null;
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
                        (_thrFileVolProc != null)
                     &&
                        (
                                (_thrFileVolProc.ThreadState == ThreadState.Running)
                            ||
                                (_thrFileVolProc.ThreadState == ThreadState.WaitSleepJoin)
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
                        (_thrFileVolProc != null)
                     &&
                        (_thrFileVolProc.IsAlive)
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
                    _thrFileVolProc.Priority = _threadPriority;                    
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

        // ��������� ������ ��� ������������ ����� ����
        private FileNamer _eFileNamer;

        // ���� � ������ ��� ���������
        private String _path;

        // ��� �����
        private String _filename;
            
        // ���������� �������� �����
        private int _dataCount;

        // ���������� ����� ��� ��������������
        private int _eccCount;
                               
        // �������� �����        
        private byte[] _buffer;

        // ������ ��������� ������ (64 ��)
        private int _bufferLength;

        // ����� ��� �������������� ��������� ������������� ������� �������� ������
        private byte[] _dataLengthArr;

        // �������� ������ ��������� �������� ���������?
        private bool _finished;

        // ��������� ����������� ���������?
        private bool _processedOK;
        
        // ����� ��������� (����������) ����� �� ���������
        private Thread _thrFileVolProc;

        // ��������� �������� ��������� (����������) �����
        private ThreadPriority _threadPriority;

        // ������� ����������� ��������� ������
        private ManualResetEvent[] _exitEvent;

        // ������� ����������� ��������� ������
        private ManualResetEvent[] _executeEvent;

        // �������, ��������������� �� ���������� ���������
        private ManualResetEvent[] _finishedEvent;
                
        #endregion Data

    } // class FileSplitter
}
