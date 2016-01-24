/*----------------------------------------------------------------------+
 |	filename:	ProcessForm.cs                                          |
 |----------------------------------------------------------------------|
 |	version:	1.14                                                    |
 |	revision:   02/01/2007	08:57                                       |
 |  author:		�������� ���� ��������� (DrAF)                         |
 |	e-mail:		draf@mail.ru                                            |
 |	purpose:	������ � �������                                        |
 +----------------------------------------------------------------------+
 |  ���������������� ����������� �� ���� RAID ������                    |
 +----------------------------------------------------------------------*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.IO;

namespace RecoveryStar
{    
    /// <summary>
    /// ������� ���������� ���������� ��������
    /// </summary>
    /// <param name="text">��������� ��������</param>
    public delegate void OnUpdateStringValueHandler(String text);
    
    /// <summary>
    /// ������� ���������� ��������� ��������
    /// </summary>        
    /// <param name="value">�������� ��������</param>
    public delegate void OnUpdateDoubleValueHandler(double value);

    /// <summary>
    /// ������� ������ ���������� �������� ������ � ��������
    /// </summary>
    /// <param name="text">��������� ��������</param>
    /// <param name="value">�������� ��������</param>
    public delegate void OnUpdateStringAndDoubleValueHandler(String text, double value);

    /// <summary>
    /// ������� ��� ����������
    /// </summary>
    public delegate void OnUpdateControlsHandler();

    /// <summary>
    /// ������ ������ ���������� (������, ��������������, �������, ������������)
    /// </summary>
    public enum RSModes {None, Protect, Recover, Repair, Test};

    public partial class ProcessForm : Form
    {
        #region Construction & Destruction

        public ProcessForm()
        {
            InitializeComponent();

            // �������������� ��������� ������ ��� �������� (����������) ����� �����
            // � ���������� ������
            _eFileNamer = new FileNamer();

            // ������� ��������� ������ ���� RecoveryStar
            _eRecoveryStarCore = new RecoveryStarCore();
         
            // ������������� �� ��������� ���������
            _eRecoveryStarCore.OnUpdateFileSplittingProgress = new OnUpdateFileSplittingProgressHandler(OnUpdateFileSplittingProgress);
            _eRecoveryStarCore.OnFileSplittingFinish = new OnFileSplittingFinishHandler(OnFileSplittingFinish);

            _eRecoveryStarCore.OnUpdateRSMatrixFormingProgress = new OnUpdateRSMatrixFormingProgressHandler(OnUpdateRSMatrixFormingProgress);
            _eRecoveryStarCore.OnRSMatrixFormingFinish = new OnRSMatrixFormingFinishHandler(OnRSMatrixFormingFinish);
            _eRecoveryStarCore.OnUpdateFileCodingProgress = new OnUpdateFileCodingProgressHandler(OnUpdateFileCodingProgress);
            _eRecoveryStarCore.OnFileCodingFinish = new OnFileCodingFinishHandler(OnFileCodingFinish);

            _eRecoveryStarCore.OnUpdateFileAnalyzeProgress = new OnUpdateFileAnalyzeProgressHandler(OnUpdateFileAnalyzeProgress);
            _eRecoveryStarCore.OnFileAnalyzeFinish = new OnFileAnalyzeFinishHandler(OnFileAnalyzeFinish);
            _eRecoveryStarCore.OnGetDamageStat = new OnGetDamageStatHandler(OnGetDamageStat);

            // �������������� ������ ������ ��� ���������
            _filenamesToProcess = new ArrayList();

            // ��������� �������� � �������� ����������, �������������� ��
            // ��������� �������� ��������� ������
            SetThreadPriority(processPriorityComboBox.SelectedIndex);
            
            // �������������� ������� ����������� ��������� �����
            _exitEvent = new ManualResetEvent[] { new ManualResetEvent(false) };

            // �������������� c������ ����������� ��������� �����
            _executeEvent = new ManualResetEvent[] { new ManualResetEvent(false) };

            // �������������� c������ "�����������" ����� ��������
            _wakeUpEvent = new ManualResetEvent[] { new ManualResetEvent(false) };
            
            // ������������� �������� ��-��������� ��� ����������...
            processPriorityComboBox.Text = "��-���������";
            //...� ��������� ����� �� ������, �������� ��-���������
            pauseButton.Focus();
        }

        #endregion Construction & Destruction
    
        #region Public Operations

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
        /// ����� ��������� ������
        /// </summary>
        private void Process()
        {           
            // ����� ��������������� �����
            int fileNum = 0;

            // ���������� ���������� ���������
            _OKCount = 0;
            _errorCount = 0;

            // ������, �������� �������� ������ ���������� �������������� ������
            String filesTotal = Convert.ToString(_filenamesToProcess.Count);

            // ������, �������� ����� ��� ������ �� �����
            String textToOut = "";
            
            // ������������ ��� ����� �� ��������������� ������
            foreach (String fullFilename in _filenamesToProcess)
            {                
                // �������� �������� ������� �������� �����
                String shortFilename = _eFileNamer.GetShortFilename(fullFilename);

                // ��� ����� ��� ������ �� �����
                String unpackedFilename = shortFilename;

                // ���� ������������ ����� �� ������ ������,
                // ��������� ���������� ����� �� ����������� �������
                if (_mode != RSModes.Protect)
                {
                    // ������������� �������� ������� ����� � ���������� �������������
                    unpackedFilename = shortFilename;

                    // ���� �� ������� ��������� ����������� �������� ��� - ���������
                    // �� ��������� ��������
                    if (!_eFileNamer.Unpack(ref unpackedFilename))
                    {
                        continue;
                    }
                }

                // �������������� ����� ��� ������ � ��������� �����
                if (_mode == RSModes.Protect) { textToOut = "������ ����� \""; }
                if (_mode == RSModes.Recover) { textToOut = "���������� ����� \""; }
                if (_mode == RSModes.Repair)  { textToOut = "������� ����� ����� \""; }
                if (_mode == RSModes.Test)    { textToOut = "������������ ����� \""; }

                textToOut += unpackedFilename + "\" (" + Convert.ToString(++fileNum) + " �� " + filesTotal + ")";                
                // ������� ����� � ��������� �����
                if (this.InvokeRequired) this.Invoke(((OnUpdateStringValueHandler)delegate(String value) { this.Text = value; }), new object[] { textToOut });                               

                // ���� ������� ����� ������ ������
                if (_mode == RSModes.Protect)
                {
                    // ��������� �� �������� ����������, ������� �� �����
                    // �������������� � ��������� �������� ��������
                    if (fileAnalyzeStatGroupBox.InvokeRequired) this.Invoke(((OnUpdateControlsHandler)delegate() { fileAnalyzeStatGroupBox.Enabled = false; }), new object[] { });
                    if (percOfDamageLabel.InvokeRequired) this.Invoke(((OnUpdateControlsHandler)delegate() { percOfDamageLabel.Enabled = false; }), new object[] { });
                    if (percOfAltEccLabel.InvokeRequired) this.Invoke(((OnUpdateControlsHandler)delegate() { percOfAltEccLabel.Enabled = false; }), new object[] { });                    

                    // ��������� ���������������� �����������
                    _eRecoveryStarCore.StartToProtect(fullFilename, _dataCount, _eccCount, true);
                }

                // ���� ������� ����� ��� ��������������
                if (_mode == RSModes.Recover)
                {
                    // ��������� �������������� ������
                    _eRecoveryStarCore.StartToRecover(fullFilename, true);
                }

                // ���� ������� ����� �������
                if (_mode == RSModes.Repair)
                {
                    // ��������� �������������� ������
                    _eRecoveryStarCore.StartToRepair(fullFilename, true);
                }

                // ���� ������� ����� ������������
                if (_mode == RSModes.Test)
                {
                    // ��������� �������������� ������
                    _eRecoveryStarCore.StartToTest(fullFilename, true);
                }

                // ���� ��������� ���������
                while (true)
                {
                    // ���� �� ���������� �������������� ������� "_executeEvent",
                    // �� ������������ �����, ����� �� ��������� ��������� �� ����� -                       
                    if (!ManualResetEvent.WaitAll(_executeEvent, 0, false))
                    {
                        //...���������������� ������ ��������������� ���������...                      
                        _eRecoveryStarCore.Pause();
                      
                        //...� ���� ��������
                        ManualResetEvent.WaitAll(_executeEvent);

                        // � ����� ����������, ���������, ��� ��������� ������ ������������
                        _eRecoveryStarCore.Continue();
                    } 

                    // ���� ����� �� ������������� �������...
                    int eventIdx = ManualResetEvent.WaitAny(new ManualResetEvent[] { _wakeUpEvent[0], _exitEvent[0], _eRecoveryStarCore.FinishedEvent[0] });

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
                        _eRecoveryStarCore.Stop();
                      
                        return;
                    }

                    //...���� �������� ������ � ���������� ��������� ��������� ����������...
                    if (eventIdx == 2)
                    {
                        //...������� �� ����� �������� ���������� (����� � ����� � while(true)!)
                        break;
                    }

                } // while(true)

                // � ����� � ��������� �������� ���������� �������� �������
                // ���������� ��������� ������ ���������, ��������� �������
                // ����������� � ���� ������. ����� ��� �� ��������, ��
                // ������������� �� ��������� ��������, ��������, ���
                // "�� ����������". ���������� ��������� ��������,
                // �� ���� - ���� �������.
                for (int i = 0; i < 9000; i++)
                {
                    if (!_eRecoveryStarCore.Finished)
                    {
                        Thread.Sleep(100);
                    }
                }

                // ���������� �������� �� ������������ ���������
                if (_eRecoveryStarCore.ProcessedOK)
                {
                    // ���� ��������� ����� ��������� ���������...                    
                    OnUpdateLogListBox(this.Text + " : OK!");

                    // �������� ����������
                    _OKCount++;

                    // ��������� ����� ��� ������ �� �����
                    textToOut = Convert.ToString(_OKCount);
                    if (OKCountLabel.InvokeRequired) this.Invoke(((OnUpdateStringValueHandler)delegate(String value) { OKCountLabel.Text = value; }), new object[] { textToOut });                   

                } else
                {
                    // ���� ��������� ����� ��������� �����������...                    
                    OnUpdateLogListBox(this.Text + " : ERROR!");

                    // �������� ����������
                    _errorCount++;

                    // ��������� ����� ��� ������ �� �����
                    textToOut = Convert.ToString(_errorCount);
                    if (errorCountLabel.InvokeRequired) this.Invoke(((OnUpdateStringValueHandler)delegate(String value) { errorCountLabel.Text = value; }), new object[] { textToOut });
                }

                // ������� ������ ��� ��������� ������ ����� ���� �� �����
                OnUpdateLogListBox("");
            }

            // ���� ������������ ����� �������������� ������, ��
            // ����� ������������� ��� �������� ����� ���������
            if (_mode == RSModes.Recover)
            {
                try
                {
                    foreach (String fullFilename in _filenamesToProcess)
                    {                                                                      
                        // ���������� ��������� ���� �� ������� ����� �����
                        String path = _eFileNamer.GetPath(fullFilename);

                        // ���������� ��������� ����� �� ������� ����� �����
                        String filename = _eFileNamer.GetShortFilename(fullFilename);
                        
                        // ���� ��� ��������� ��������������� - ��� �������� ��������,
                        // �.�. �������� ��� �� ������ ���������� ����������� ���������
                        if (!_eFileNamer.Unpack(ref filename, ref _dataCount, ref _eccCount))
                        {
                            continue;
                        }
                        
                        // ������������ ��� �����
                        for (int i = 0; i < (_dataCount + _eccCount); i++)
                        {
                            // ��������� �������������� ��� �����,...
                            String volumename = filename;

                            //...����������� ��� � ���������� ������...
                            _eFileNamer.Pack(ref volumename, i, _dataCount, _eccCount);

                            //...��������� ������ ��� �����...
                            volumename = path + volumename;

                            //...���������� ���� �� ������� �����...                    
                            if (File.Exists(volumename))
                            {
                                //...���� ������� �������, ������ �� ���� ��������
                                // ��-���������...
                                File.SetAttributes(volumename, FileAttributes.Normal);
                                //...� ����� �������
                                File.Delete(volumename);
                            }
                        }
                    }
                }

                catch (IOException e) { }
            }
            
            // ��������� ����� ��� ������...
            textToOut = "�������";
            //...�������� ������� �� ������ ����������� ���������...
            if (stopButton.InvokeRequired) this.Invoke(((OnUpdateStringValueHandler)delegate(String value) { stopButton.Text = value; }), new object[] { textToOut });
            
            //...� ��������� ������ "�����"...
            if (pauseButton.InvokeRequired) this.Invoke(((OnUpdateControlsHandler)delegate() { pauseButton.Enabled = false; }), new object[] { });

            //...� ���������� ������ ������ ���������� ��������
            if (processPriorityComboBox.InvokeRequired) this.Invoke(((OnUpdateControlsHandler)delegate() { processPriorityComboBox.Enabled = false; }), new object[] { });            
        }
       
        /// <summary>
        /// ����� ��������� ���������� �������� ��������� �� ��������� ����������� �������� int
        /// </summary>
        /// <param name="value"></param>
        private void SetThreadPriority(int value)
        {
            if (
                    (_thrRecoveryStarProcess != null)
                 &&
                    (_thrRecoveryStarProcess.IsAlive)
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

                // ������������� ��������� ���������
                _thrRecoveryStarProcess.Priority = _threadPriority;             
                _eRecoveryStarCore.ThreadPriority = value;
            }
        }

        /// <summary>
        /// ����� ���������� �������� � ��������� �������� ���������� "processGroupBox"
        /// </summary>
        /// <param name="text">�����, ����������� ���������� ��������</param>
        /// <param name="progress">���������� �������� ���������</param>
        private void OnUpdateProgressGroupBox(String text, double progress)
        {
            if (processGroupBox.InvokeRequired) processGroupBox.Invoke(new OnUpdateStringAndDoubleValueHandler(OnUpdateProgressGroupBox), new object[] { text, progress });            
            else
            {
                processGroupBox.Text = " " + text + ": " + Convert.ToString((int)(progress) + " %");
            }
        }

        /// <summary>
        /// ����� ���������� ��������, ������������ ���������
        /// � ��������� �������� ���������� "processGroupBox"
        /// </summary>
        /// <param name="text">����� ��� ������</param>    
        private void OnFinishProgressGroupBox(String text)
        {
            if (processGroupBox.InvokeRequired) processGroupBox.Invoke(new OnUpdateStringValueHandler(OnFinishProgressGroupBox), new object[] { text });
            else
            {
                processGroupBox.Text = " " + text + ": ���������";

                // ������� 100% � "processProgressBar"
                OnUpdateProcessProgressBar(100.0);
            }
        }

        /// <summary>
        /// ����� �������� ��������� � ������� ���������� "processProgressBar"
        /// </summary>        
        /// <param name="progress">���������� �������� ���������</param>
        private void OnUpdateProcessProgressBar(double progress)
        {
            if (processProgressBar.InvokeRequired) processProgressBar.Invoke(new OnUpdateDoubleValueHandler(OnUpdateProcessProgressBar), new object[] { progress });
            else
            {
                processProgressBar.Value = (int)progress;
            }
        }

        /// <summary>
        /// ���������� ������� "���������� ��������� ��������� �����"
        /// </summary>
        /// <param name="progress">�������� ��������� � ���������</param>
        private void OnUpdateFileSplittingProgress(double progress)
        {
            OnUpdateProgressGroupBox("��������� �����", progress);
            OnUpdateProcessProgressBar(progress);
        }

        /// <summary>
        /// ���������� ������� "���������� �������� ��������� �����"
        /// </summary>        
        private void OnFileSplittingFinish()
        {            
            OnFinishProgressGroupBox("��������� �����");
        }

        /// <summary>
        /// ���������� ������� "���������� ��������� ������� ������� ����������� ����-��������"
        /// </summary>      
        private void OnUpdateRSMatrixFormingProgress(double progress)
        {            
            OnUpdateProgressGroupBox("������ ������� �����������", progress);
            OnUpdateProcessProgressBar(progress);
        }

        /// <summary>
        /// ���������� ������� "���������� ������� ������� ����������� ����-��������"
        /// </summary>        
        private void OnRSMatrixFormingFinish()
        {            
            OnFinishProgressGroupBox("������ ������� �����������");
        }

        /// <summary>
        /// ���������� ������� "���������� ��������� �������� ����������� �����"
        /// </summary>        
        private void OnUpdateFileCodingProgress(double progress)
        {            
            OnUpdateProgressGroupBox("����������� ����-��������", progress);
            OnUpdateProcessProgressBar(progress);
        }

        /// <summary>
        /// ���������� ������� "���������� �������� ����������� �����"
        /// </summary>        
        private void OnFileCodingFinish()
        {            
            OnFinishProgressGroupBox("����������� ����-��������");
        }

        /// <summary>
        /// ���������� ������� "���������� ��������� �������� ������� �����"
        /// </summary>        
        private void OnUpdateFileAnalyzeProgress(double progress)
        {            
            OnUpdateProgressGroupBox("�������� ����������� ������", progress);
            OnUpdateProcessProgressBar(progress);
        }

        /// <summary>
        /// ���������� ������� "���������� �������� ������� �����"
        /// </summary>        
        private void OnFileAnalyzeFinish()
        {
            OnFinishProgressGroupBox("�������� ����������� ������");
        }

        /// <summary>
        /// ���������� ������� "��������� ���������� ����������� �����"
        /// </summary>        
        private void OnGetDamageStat(double percOfDamage, double percOfAltEcc)
        {
            if (this.InvokeRequired) this.Invoke(new OnGetDamageStatHandler(OnGetDamageStat), new object[] { percOfDamage, percOfAltEcc });
            else
            {
                // ������� ���������� ���������� ���������� ������� �� �����
                percOfDamageLabel.Text = "����� ������������ �����: ";
                percOfAltEccLabel.Text = "������ ����� ��� ��������������: ";
                                                
                // ����������� ������, ���������� ����� ���������
                if (
                        (percOfDamage == -1)
                     &&
                        (percOfAltEcc == -1)
                    )
                {
                    // ���� ��� �������� ���� ���������� - ������� ���������� � ���...                    
                    logListBox.Items.Add(this.Text + " : ��� �������� ���� ����������!");

                    //...� �������
                    return;
                }
                
                if (
                        (percOfDamage == 0)
                     &&
                        (percOfAltEcc == -1)
                    )
                {
                    // ���� ��� ����������� � ������� �������� ����� - ������� ���������� � ���...                    
                    logListBox.Items.Add(this.Text + " : ��� ����������� �������� �����!");
                    
                    //...� �������
                    return;
                }

                // ������� ���������� �����������                
                percOfDamageLabel.Text = "����� ������������ �����: " + Convert.ToString((int)(percOfDamage)) + " %";
                percOfAltEccLabel.Text = "������ ����� ��� ��������������: " + Convert.ToString((int)(percOfAltEcc)) + " %";
                logListBox.Items.Add("����� ������������ �����: " + Convert.ToString((int)(percOfDamage)) + " %");
                logListBox.Items.Add("������ ����� ��� ��������������: " + Convert.ToString((int)(percOfAltEcc)) + " %");                
            }
        }

        /// <summary>
        /// ���������� ������� "���������� ���� �������� ���������"
        /// </summary>        
        private void OnUpdateLogListBox(String text)
        {
            if (logListBox.InvokeRequired) logListBox.Invoke(new OnUpdateStringValueHandler(OnUpdateLogListBox), new object[] { text });
            else
            {
                logListBox.Items.Add(text);
            }
        }
      
        private void stopButton_Click(object sender, EventArgs e)
        {
            // ����������� ��������� ������...
            Stop();
            Close();
        }

        private void ProcessForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            // ����������� ��������� ������...
            Stop();
        }

        private void ProcessForm_Load(object sender, EventArgs e)
        {      
            // ���������, ��� ����� ������ �����������
            _exitEvent[0].Reset();
            _executeEvent[0].Set();
          
            // ������� ����� ��������� ������...
            _thrRecoveryStarProcess = new Thread(new ThreadStart(Process));

            //...����� ���� ��� ���...
            _thrRecoveryStarProcess.Name = "RecoveryStar.Process()";

            //...������������� ��������� ��������� ������...
            _thrRecoveryStarProcess.Priority = _threadPriority;

            //...� ��������� ���
            _thrRecoveryStarProcess.Start();
        }

        private void pauseButton_Click(object sender, EventArgs e)
        {
            if (pauseButton.Text == "�����")
            {
                pauseButton.Image = global::RecoveryStar.Properties.Resources.Start;
                pauseButton.Text = "����������";

                // ������ ��������� �� �����
                Pause();

            } else
            {
                pauseButton.Image = global::RecoveryStar.Properties.Resources.Pause;
                pauseButton.Text = "�����";

                // ������� ��������� � �����
                Continue();                
            }
        }

        private void processPriorityTrackBar_ValueChanged(object sender, EventArgs e)
        {            
            // ��������� �������� � �������� ����������, �������������� ��
            // ��������� �������� ��������� ������
            SetThreadPriority(processPriorityComboBox.SelectedIndex);
        }

        private void processPriorityComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            pauseButton.Focus();            
        }

        #endregion Private Operations

        #region Public Properties
       
        /// <summary>
        /// ������ ������ ���� ������ ��� ���������
        /// </summary>
        public ArrayList FilenamesToProcess
        {
            get
            {
                return _filenamesToProcess;
            }

            set
            {
                _filenamesToProcess = value;
            }
        }
      
        /// <summary>
        /// ���������� �������� �����
        /// </summary>
        public int DataCount
        {
            get
            {                
                return _dataCount;                
            }

            set
            {
                _dataCount = value;
            }
        }

        /// <summary>
        /// ���������� ����� ��� ��������������
        /// </summary>
        public int EccCount
        {
            get
            {
                return _eccCount;
            }

            set
            {
                _eccCount = value;
            }
        }

        /// <summary>
        /// ������������ ����� �������������� ������?
        /// </summary>
        public RSModes Mode
        {
            get
            {
                return _mode;
            }

            set
            {
                _mode = value;
            }
        }
       
        #endregion Public Properties

        #region Data
        
        // ������� ��������� ������ ��� �������� (����������) ����� ����� � ���������� ������            
        FileNamer _eFileNamer;

        // ���� ������� ����������������� �����������
        RecoveryStarCore _eRecoveryStarCore;
       
        // ������ ������ ���� ������ ��� ���������
        private ArrayList _filenamesToProcess;

        // ���������� �������� �����
        private int _dataCount;

        // ���������� ����� ��� ��������������
        private int _eccCount;

        // ������������ ����� �������������� ������?
        private RSModes _mode;

        // ������� ��������� ������������ ������
        private int _OKCount;
        
        // ������� ����������� ������������ ������
        private int _errorCount;

        // ����� ��������� ������
        private Thread _thrRecoveryStarProcess;

        // ��������� �������� ��������� ������
        private ThreadPriority _threadPriority;

        // ������� ����������� ���������
        private ManualResetEvent[] _exitEvent;

        // ������� ����������� ���������
        private ManualResetEvent[] _executeEvent;

        // ������� "�����������" ����� ��������
        private ManualResetEvent[] _wakeUpEvent;
  
        #endregion Data        
    }        
}