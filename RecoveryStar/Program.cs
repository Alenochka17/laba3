/*----------------------------------------------------------------------+
 |	filename:	Programs.cs                                             |
 |----------------------------------------------------------------------|
 |	version:	1.14                                                    |
 |	revision:   02/03/2007	08:59                                       |
 |  author:		�������� ���� ��������� (DrAF)                         |
 |	e-mail:		draf@mail.ru                                            |
 |	purpose:	������ � �������                                        |
 +----------------------------------------------------------------------+
 |  ���������������� ����������� �� ���� RAID ������                    |
 +----------------------------------------------------------------------*/

using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace RecoveryStar
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}