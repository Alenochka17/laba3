/*----------------------------------------------------------------------+
 |	filename:	AboutForm.cs                                            |
 |----------------------------------------------------------------------|
 |	version:	1.14                                                    |
|	revision:   01/03/2007	09:13                                       |
 |  author:		Дробанов Артём Федорович (DrAF)                         |
 |	e-mail:		draf@mail.ru                                            |
 |	purpose:	Работа с файлами                                        |
 +----------------------------------------------------------------------+
 |  Отказоустойчивое кодирование по типу RAID систем                    |
 +----------------------------------------------------------------------*/

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace RecoveryStar
{
    public partial class AboutForm : Form
    {
        #region Construction & Destruction

        public AboutForm()
        {
            InitializeComponent();

            _timerCounter = 0;
        }
        
        #endregion Construction & Destruction

        #region Private Operations

        private void imitLinkLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            // Организуем переход по соответствующей метке
            System.Diagnostics.Process.Start(imitLinkLabel.Text);
        }

        private void okButton_Click(object sender, EventArgs e)
        {
            Close();
        }

        /// <summary>
        /// Обработчик анимации
        /// </summary>        
        private void RSIconTimer_Tick(object sender, EventArgs e)
        {
            switch (_timerCounter)
            {
                case 0:  { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_0;  break; }
                case 1:  { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_1;  break; }
                case 2:  { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_2;  break; }
                case 3:  { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_3;  break; }
                case 4:  { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_4;  break; }
                case 5:  { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_5;  break; }
                case 6:  { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_6;  break; }
                case 7:  { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_7;  break; }
                case 8:  { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_8;  break; }
                case 9:  { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_9;  break; }
                case 10: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_10; break; }
                case 11: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_11; break; }
                case 12: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_12; break; }
                case 13: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_13; break; }
                case 14: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_14; break; }
                case 15: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_15; break; }
                case 16: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_16; break; }
                case 17: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_17; break; }
                case 18: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_18; break; }
                case 19: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_19; break; }
                case 20: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_20; break; }
                case 21: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_21; break; }                
                case 22: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_22; break; }
                case 23: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_23; break; }
                case 24: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_24; break; }
                case 25: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_25; break; }
                case 26: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_26; break; }
                case 27: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_27; break; }
                case 28: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_28; break; }
                case 29: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_29; break; }
                case 30: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_30; break; }                
                case 31: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_31; break; }
                case 32: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_32; break; }
                case 33: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_33; break; }
                case 34: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_34; break; }
                case 35: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_35; break; }
                case 36: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_36; break; }
                case 37: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_37; break; }
                case 38: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_38; break; }
                case 39: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_39; break; }
                case 40: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_40; break; }
                case 41: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_41; break; }
                case 42: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_42; break; }
                case 43: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_43; break; }
                case 44: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_44; break; }
                case 45: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_45; break; }
                case 46: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_46; break; }
                case 47: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_47; break; }
                case 48: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_48; break; }
                case 49: { logoPictureBox.Image = global::RecoveryStar.Properties.Resources.RSIcon_49; break; }
            }

            _timerCounter++;

            if (_timerCounter > 49)
            {
                _timerCounter = 0;
            }
        }
       
        private void AboutForm_Load(object sender, EventArgs e)
        {
            RSIconTimer.Enabled = true;
        }

        private void AboutForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            RSIconTimer.Enabled = false;
        }

        #endregion Private Operations

        #region Data

        private int _timerCounter;

        #endregion Data
    }
}