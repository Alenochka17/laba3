/*----------------------------------------------------------------------+
 |	filename:	MainForm.cs                                             |
 |----------------------------------------------------------------------|
 |	version:	1.14                                                    |
 |	revision:   01/03/2007	15:08                                       |
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
using System.IO;

namespace RecoveryStar
{
    public partial class MainForm : Form
    {
        #region Construction & Destruction

        public MainForm()
        {
            InitializeComponent();

            // �������������� ��������� ������ ��� �������� (����������) ����� �����
            // � ���������� ������
            _eFileNamer = new FileNamer();
        }

        #endregion Construction & Destruction

        #region Private Operations

        /// <summary>
        /// ����� ��������� ������ � ��������� ����������
        /// </summary>
        private void ProcessFiles()
        {
            // ���� � �������� � �������� �������� �������� ������� ����������
            if (browser.SelectedItem.IsFolder)
            {
                // ���� � ������ ������ ����� ������� - ������ �������
                if (
                        (_eProcessForm != null)
                     &&
                        (_eProcessForm.Visible)
                    )
                {
                    return;
                }

                // ������������� ��������� ������
                _eProcessForm.DataCount = _dataCount;
                _eProcessForm.EccCount  = _eccCount;

                // �������� ����� ������ ��� ���������                                                
                for (int i = 0; i < browser.SelectedItem.SubFiles.Count; i++)
                {
                    // �������� �������� ������� ����� �����
                    String shortFilename = _eFileNamer.GetShortFilename(((ShellDll.ShellItem)browser.SelectedItem.SubFiles.Items[i]).Path);

                    // ���� ��� ��������� ����� ��������� 50 �������� - �� �� ����� ���� ���������
                    if (shortFilename.Length > 50)
                    {
                        string message = "����� ����� ����� \"" + shortFilename + "\" ��������� 50 ��������! ���������� ���� ���� � ���������� ������� ������������ ������ ��� ���������?";
                        string caption = " Recovery Star 1.14";
                        MessageBoxButtons buttons = MessageBoxButtons.YesNo;
                        DialogResult result;
                        result = MessageBox.Show(null, message, caption, buttons, MessageBoxIcon.Exclamation, MessageBoxDefaultButton.Button1);

                        // ���� ������������ ����� �� ������ "No" - ��������� ���������
                        if (result == DialogResult.No)
                        {
                            return;
                        }

                    } else
                    {
                        _eProcessForm.FilenamesToProcess.Add(((ShellDll.ShellItem)browser.SelectedItem.SubFiles.Items[i]).Path);
                    }
                }

                // ���� ������ ������ ��� ��������� �� ����� ����
                // (�.�. ���� ��� ������������) - ����� ������������ ���������
                if (_eProcessForm.FilenamesToProcess.ToArray().Length != 0)
                {
                    // ������� ���� ������� � �����������, ����� �� ��������� ������� ���������
                    browser.BrowserUp();

                    // ���������� ���������� ���� ���������
                    _eProcessForm.Show();

                } else
                {
                    string message = "� ��������� ���������� �� ������� ������ ��� ���������!";
                    string caption = " Recovery Star 1.14";
                    MessageBoxButtons buttons = MessageBoxButtons.OK;
                    MessageBox.Show(null, message, caption, buttons, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1);
                }
            }
        }

        /// <summary>
        /// ����� ��������� ������ � ��������� ���������� � ������ ����������� �����,
        /// ����������� ������������������
        /// </summary>
        private void ProcessUniqueFiles()
        {
            // ���� � �������� � �������� �������� �������� ������� ����������
            if (browser.SelectedItem.IsFolder)
            {
                // ���� � ������ ������ ����� ������� - ������ �������
                if (
                        (_eProcessForm != null)
                     &&
                        (_eProcessForm.Visible)
                    )
                {
                    return;
                }

                // ������ ���������� ���� ������ ��� ���������
                ArrayList uniqueNamesToProcess = new ArrayList();

                // �������� ����� ������ ��� ���������                                                
                for (int i = 0; i < browser.SelectedItem.SubFiles.Count; i++)
                {
                    // ��������� ��������� ��� �� ������...
                    String fullFilename = (String)((ShellDll.ShellItem)browser.SelectedItem.SubFiles.Items[i]).Path;

                    //...�������� ��� �������� �������...
                    String shortFilename = _eFileNamer.GetShortFilename(fullFilename);

                    //...� ������������� ��� � ���������� ������������� �����...
                    String unpackedFilename = shortFilename;

                    // ���� �� ������� ��������� ����������� �������� ��� - ���������
                    // �� ��������� ��������
                    if (!_eFileNamer.Unpack(ref unpackedFilename))
                    {
                        continue;
                    }

                    //...����� ��������� ��� �� ������������ - ���� ����� ��� ��� ����
                    // � ������� "uniqueNamesToProcess", �� ��������� ��� �� �����

                    // ������� ������������, ��� ������������� ��� ����� ���������
                    bool unpackedFilenameIsUnique = true;

                    // ���������� ���� ��������� ������ ���������� ����
                    foreach (String currUnpackedFilename in uniqueNamesToProcess)
                    {
                        // ���� ���������� ���������� - ��� �� ���������,
                        // �������� �� ���� � ������� �� ������
                        if (currUnpackedFilename == unpackedFilename)
                        {
                            unpackedFilenameIsUnique = false;

                            break;
                        }
                    }

                    // ���� ������������� ���� ��������...
                    if (unpackedFilenameIsUnique)
                    {
                        //...��������� ��� � ������ ���������� ����...
                        uniqueNamesToProcess.Add(unpackedFilename);

                        //...��������� ��� � ������ ��� ���������...
                        _eProcessForm.FilenamesToProcess.Add(fullFilename);
                    }
                }

                // ���� ������ ������ ��� ��������� �� ����� ����
                // (�.�. ���� ��� ������������) - ����� ������������ ���������
                if (_eProcessForm.FilenamesToProcess.ToArray().Length != 0)
                {
                    // ������� ���� ������� � �����������, ����� �� ��������� ������� ���������
                    browser.BrowserUp();

                    // ���������� ���������� ���� ���������
                    _eProcessForm.Show();

                } else
                {
                    string message = "� ��������� ���������� �� ������� ������ ��� ���������!";
                    string caption = " Recovery Star 1.14";
                    MessageBoxButtons buttons = MessageBoxButtons.OK;
                    MessageBox.Show(null, message, caption, buttons, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1);
                }
            }
        }

        /// <summary>
        /// ������ ������ ����������� �����
        /// </summary>        
        private void protectButton_Click(object sender, EventArgs e)
        {
            // ����� ����� �� ������ ��������� ����� �� �������� �������
            browser.Focus();

            // ������� ����� ����������� �����
            _eProcessForm = new ProcessForm();

            // ��������� �����, ���� ���� ������� � ����� ���������
            if (_eProcessForm.Visible)
            {
                _eProcessForm.Close();
            }

            // ������������� ����� ������
            _eProcessForm.Mode = RSModes.Protect;

            // ��������� ��������� �����
            ProcessFiles();
        }

        /// <summary>
        /// ������ ������ �������������� ������ �����
        /// </summary>        
        private void recoverButton_Click(object sender, EventArgs e)
        {
            // ����� ����� �� ������ ��������� ����� �� �������� �������
            browser.Focus();

            // ������� ����� ����������� �����
            _eProcessForm = new ProcessForm();

            // ��������� �����, ���� ���� ������� � ����� ���������
            if (_eProcessForm.Visible)
            {
                _eProcessForm.Close();
            }

            // ������������� ����� ������
            _eProcessForm.Mode = RSModes.Recover;

            // ��������� ��������� �����
            ProcessUniqueFiles();
        }

        /// <summary>
        /// ������ ������ ������� ������ ������ �����
        /// </summary>        
        private void repairButton_Click(object sender, EventArgs e)
        {
            // ����� ����� �� ������ ��������� ����� �� �������� �������
            browser.Focus();

            // ������� ����� ����������� �����
            _eProcessForm = new ProcessForm();

            // ��������� �����, ���� ���� ������� � ����� ���������
            if (_eProcessForm.Visible)
            {
                _eProcessForm.Close();
            }

            // ������������� ����� ������
            _eProcessForm.Mode = RSModes.Repair;

            // ��������� ��������� �����
            ProcessUniqueFiles();
        }

        /// <summary>
        /// ������ ������ ������������ ������ ������ �����
        /// </summary>        
        private void testButton_Click(object sender, EventArgs e)
        {
            // ����� ����� �� ������ ��������� ����� �� �������� �������
            browser.Focus();

            // ������� ����� ����������� �����
            _eProcessForm = new ProcessForm();

            // ��������� �����, ���� ���� ������� � ����� ���������
            if (_eProcessForm.Visible)
            {
                _eProcessForm.Close();
            }

            // ������������� ����� ������
            _eProcessForm.Mode = RSModes.Test;

            // ��������� ��������� �����
            ProcessUniqueFiles();
        }

        /// <summary>
        /// ����� ��������� ������������ ������ � ������������ ���������������
        /// ��������� �� �����
        /// </summary>
        private void SetCoderConfig()
        {
            // �������������� �������, �������� ����������� �������� ������� ���� � ������������,
            // ��������� ������������
            _allVolCountTrackBarValuesArr = new int[] {4, 8, 16, 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384};
            _redundancyTrackBarValuesArr = new int[] {5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70, 75, 80, 85, 90, 95, 100};

            // ������� ������ � ��������� ���������� (����� ���������� ����� � ������������)
            _allVolCount = _allVolCountTrackBarValuesArr[allVolCountMacTrackBar.Value];
            _redundancy = _redundancyTrackBarValuesArr[redundancyMacTrackBar.Value];

            // ������������� ��������� �����, ��������������� ��������� ��������� ���������� "TrackBar"
            allVolCountGroupBox.Text = " ����� ���������� �����: " + System.Convert.ToString(_allVolCount);
            redundancyGroupBox.Text = " ������������ �����������: " + System.Convert.ToString(_redundancy) + " %";

            // ���������� �������� ���������� ��������� �� ���
            double percByVol = (double)_allVolCount / (double)200;

            // ��������� ���������� ����� ��� ��������������
            _eccCount = (int)((double)_redundancy * percByVol); // ����� ��� ��������������

            // � ������ ������������� ������������ ���������� ����� ��� ��������������
            if (_eccCount < 1)
            {
                _eccCount = 1;
            }

            // ���������� �������� ����� ������� �� ����������� ��������
            _dataCount = _allVolCount - _eccCount;

            // ���� ���������� �����
            while (_eccCount >= _dataCount)
            {
                _eccCount--;
                _dataCount++;
            }

            // ������������ ������ ����� ���������� ����� (�������� �������� ��������� ������
            // � ������ ����-��������, �.�. ������� ������������� ������� ���������)
            // �� ��� �������� ��������� ������ �����, ����� ���� ������ �� ����������������!
            if (
                    (_eccCount > 1)
                 &&
                    ((_dataCount % 2) != 0)                
                )
            {
                _eccCount--;
                _dataCount++;
            }

            // ��������� �������������� �����
            String outX = System.Convert.ToString((((double)(_dataCount + _eccCount)) / (double)_dataCount));
            
            // ����� ���������, ���������� ��-���������
            int subStrLen = 6;
    
            // ������������ (� ������ ����������) ����� ����������� ���������
            if (outX.Length < 6)
            {
                subStrLen = outX.Length;
            }

            // ������� ������������ ������
            coderConfigGroupBox.Text = " ������������ ������ (�������� �����: " + System.Convert.ToString(_dataCount)
                                     + "; ����� ��� ��������������: " + System.Convert.ToString(_eccCount)
                                     + "; ����� ������: " + outX.Substring(0, subStrLen)
                                     + " X)";
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            // ������������� ����� ���������� ����� ��-��������� - 1024
            allVolCountMacTrackBar.Value = 8;

            // ������������ ������������ ����������� - 50%
            redundancyMacTrackBar.Value = 9;

            // ������������� ������������ ������
            SetCoderConfig();
        }

        private void allVolCountMacTrackBar_ValueChanged(object sender, decimal value)
        {
            // ������������� ������������ ������
            SetCoderConfig();
        }

        private void redundancyMacTrackBar_ValueChanged(object sender, decimal value)
        {
            // ������������� ������������ ������
            SetCoderConfig();
        }

        private void ������������ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // ��������� ���� �������
            if (File.Exists("help.mht"))
            {
                System.Diagnostics.Process.Start("help.mht");
            }
        }

        private void ����������ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AboutForm eAboutForm = new AboutForm();
            eAboutForm.ShowDialog();
        }

        private void �����ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }
        
        #endregion Private Operations

        #region Data

        // ������ ��� �������� (����������) ����� � ���������� ������
        private FileNamer _eFileNamer;

        // ������� �������� �������� ��� ��������� ���������� "TrackBar" (���������� ����� � ������������)
        private int[] _allVolCountTrackBarValuesArr;
        private int[] _redundancyTrackBarValuesArr;

        // ����� ���������� �����
        private int _allVolCount;

        // ������������ �����������
        private int _redundancy;
        
        // ���������� �������� �����
        private int _dataCount;

        // ���������� ����� ��� ��������������
        private int _eccCount;

        // ����� ��������� ������
        ProcessForm _eProcessForm;

        #endregion Data                        
    }
}