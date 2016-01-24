/*----------------------------------------------------------------------+
 |	filename:	FileNamer.cs                                            |
 |----------------------------------------------------------------------|
 |	version:	1.14                                                    |
 |	revision:   02/03/2007	08:44                                       |
 |  author:		�������� ���� ��������� (DrAF)                         |
 |	e-mail:		draf@mail.ru                                            |
 |	purpose:	������ � ������� ������                                 |
 +----------------------------------------------------------------------+
 |  �������� (����������) ����� ����� � ���������� ������               |
 +----------------------------------------------------------------------*/

using System;
using System.Collections.Generic;
using System.Text;

namespace RecoveryStar
{
    /// <summary>
    /// ����� ��� ������������ ����� ���� �� ������ ����������� ������ � ������������ ������
    /// </summary>
    public class FileNamer
    {
        #region Construction & Destruction

        public FileNamer()
        {
            // ������������� ������������ ����� ����� ����� �� ������� ��������,
            // ������ 64 ��������
            _maxFilenameLength = 64;

            // �������������� ������ �������������� �������� � "HEX-������"
            _HEX = new char[] { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F' };
        }

        #endregion Construction & Destruction

        #region Public Operations

        /// <summary>
        /// ���������� ����, ������� ��� �� ������� ����� �����
        /// </summary>
        /// <param name="fullFilename">������ ��� �����</param>
        /// <returns>����</returns>
        public String GetPath(String fullFilename)
        {
            int lastIdxOfSeparator = -1;

            for (int i = 0; i < fullFilename.Length; i++)
            {
                if (fullFilename[i] == '\\')
                {
                    lastIdxOfSeparator = i;
                }
            }

            if (lastIdxOfSeparator != -1)
            {
                return fullFilename.Substring(0, (lastIdxOfSeparator + 1));

            } else
            {
                return "";
            }
        }

        /// <summary>
        /// ���������� �������� ��� �����, ������� ����
        /// </summary>
        /// <param name="fullFilename">������ ��� �����</param>
        /// <returns>�������� ��� �����</returns>
        public String GetShortFilename(String fullFilename)
        {            
            int lastIdxOfSeparator = -1;

            for (int i = 0; i < fullFilename.Length; i++)
            {
                if (fullFilename[i] == '\\')
                {
                    lastIdxOfSeparator = i;
                }
            }

            if (lastIdxOfSeparator != -1)
            {
                return fullFilename.Substring(lastIdxOfSeparator + 1);

            } else
            {
                return fullFilename;
            }
        }

        /// <summary>
        /// "��������" ��������� ����� ����� � ���������� ������
        /// </summary>
        /// <param name="filename">��� ����� ��� "��������"</param>
        /// <param name="volNum">����� �������� ����</param>
        /// <param name="dataCount">���������� �������� �����</param>
        /// <param name="eccCount">���������� ����� ��� ��������������</param>  
        /// <returns>��������� ���� ��������</returns>
        public bool Pack(ref String filename, int volNum, int dataCount, int eccCount)
        {
            // ��������� ����� �������� ����� �� ����������� �������� ���
            // ��������� ����������� ��� ������������ �������� �����
            if ((filename.Length + _prefixLength) > _maxFilenameLength)
            {
                return false;
            }

            // ��������� ������ ��������, ������� ����� ��������������� �� ������
            // �������������� ������
            Char[] prefixChr = new Char[] { '@', 'V', 'V', 'V', 'V', 'N', 'N', 'N', 'N', 'M', 'M', 'M', 'M', '.' };

            // ����� ������ ������ ����
            if (!Int16ToHEX(volNum, ref prefixChr, 1))
            {
                return false;
            }

            // ����� ������ ���������� �������� ����� � ������������ ������
            if (!Int16ToHEX(dataCount, ref prefixChr, 5))
            {
                return false;
            }

            // ����� ������ ���������� ����� ��� �������������� � ������������ ������
            if (!Int16ToHEX(eccCount, ref prefixChr, 9))
            {
                return false;
            }

            // ����������� ������ �������� �������� � ������ � ������������ � ���
            // �������� ��� �����
            filename = new String(prefixChr) + filename;

            return true;
        }

        /// <summary>
        /// "����������" ����� ����� �� ����������� ������� � ��������
        /// </summary>
        /// <param name="filename">��� ����� ��� "����������"</param>
        /// <param name="volNum">����� �������� ����</param>
        /// <param name="dataCount">���������� �������� �����</param>
        /// <param name="eccCount">���������� ����� ��� ��������������</param>
        /// <returns>��������� ���� ��������</returns>
        public bool Unpack(ref String filename, ref int volNum, ref int dataCount, ref int eccCount)
        {            
            // ���� ��� ����� ��� ���������� �������� ����� ������ �������
            // �� ����������� ��������, ��� ���� ����������� ��� ����� ��
            // �������� �������������� ������� � ������ � ����� � �����,
            // �� ���������� � ���������
            if (
                    (filename.Length < 15)
                 ||
                    (filename[0] != '@')
                 ||
                    (filename[13] != '.')
                )
            {
                return false;
            }

            // �������������� ������ ��� �������� ������ �������� "HEX-�������������"
            Char[] prefixPartChr = new Char[12];

            // �������� ������ "HEX-�������������" volNum, dataCount, eccCount � ������ Char
            filename.CopyTo(1, prefixPartChr, 0, 12);

            // ��������� ����� ����            
            if (!HEXToInt16(ref volNum, prefixPartChr, 0))
            {
                return false;
            }

            // ��������� ���������� �������� �����            
            if (!HEXToInt16(ref dataCount, prefixPartChr, 4))
            {
                return false;
            }

            // ��������� ���������� ����� ��� ��������������
            if (!HEXToInt16(ref eccCount, prefixPartChr, 8))
            {
                return false;
            }

            // ������� ������� �� ����� �����
            filename = filename.Substring(14, (filename.Length - 14));

            return true;
        }
        
        /// <summary>
        /// "����������" ����� ����� �� ����������� ������� � ��������
        /// </summary>
        /// <param name="filename">��� ����� ��� "����������"</param>        
        /// <param name="dataCount">���������� �������� �����</param>
        /// <param name="eccCount">���������� ����� ��� ��������������</param>
        /// <returns>��������� ���� ��������</returns>
        public bool Unpack(ref String filename, ref int dataCount, ref int eccCount)
        {
            int volNum = 0;

            return Unpack(ref filename, ref volNum, ref dataCount, ref eccCount);
        }

        /// <summary>
        /// "����������" ����� ����� �� ����������� ������� � ��������
        /// </summary>
        /// <param name="filename">��� ����� ��� "����������"</param>        
        /// <returns>��������� ���� ��������</returns>
        public bool Unpack(ref String filename)
        {
            int volNum    = 0;
            int dataCount = 0;
            int eccCount  = 0;

            return Unpack(ref filename, ref volNum, ref dataCount, ref eccCount);
        }

        #endregion Public Operations

        #region Private Operations

        /// <summary>
        /// �������������� �������� int � ������ ������� "HEX-�������������"
        /// </summary>
        /// <param name="value">�������� ��������</param>
        /// <param name="target">������� ������ ��� ������</param>
        /// <param name="startOutPos">��������� ������� � ������� ��� ��������������</param>
        private bool Int16ToHEX(int value, ref Char[] target, int startOutPos)
        {
            // ���� ��������� ����� ������� ��� ������ ���������� �������������,
            // ������� ������� �� ���������� ������� �������� �������, ��������
            // �� ����
            if ((startOutPos + 3) > target.GetUpperBound(0))
            {
                return false;
            }

            // ����������� ������ ���������� � ������ ��������� ������������� "HEX"
            target[startOutPos + 3] = _HEX[((value & 0x0000000F) >> 0)];
            target[startOutPos + 2] = _HEX[((value & 0x000000F0) >> 4)];
            target[startOutPos + 1] = _HEX[((value & 0x00000F00) >> 8)];
            target[startOutPos + 0] = _HEX[((value & 0x0000F000) >> 12)];

            return true;
        }

        /// <summary>
        /// �������������� ������� �������� "HEX-�������������" � int16
        /// </summary>
        /// <param name="value">������������ ��������</param>
        /// <param name="source">�������� ������ "HEX-������"</param>
        /// <param name="startOutPos">��������� ������� �� ������� ������� ��� ��������������</param>
        private bool HEXToInt16(ref int value, Char[] source, int startInPos)
        {
            // ���� ��������� ����� ������� ��� ������ ���������� �������������,
            // ������� ������� �� ���������� ������� �������� �������, ��������
            // �� ����
            if ((startInPos + 3) > source.GetUpperBound(0))
            {
                return false;
            }

            // ���������� �������� ������������ ��������
            value = 0;

            // ����������� ������ ��������� ������������� "HEX" � ������ ���������� "int"
            for (int i = startInPos, shift = 12; i < (startInPos + 4); i++, shift -= 4)
            {
                // ���������� ��������, ��� ������ �� ������ � ������� "_HEX"
                bool letterSearchRes = false;

                // ��������� ���������� ������� � �������
                int letterIdx = 0;

                // ����������� �� ����� ������� "_HEX"
                for (int j = 0; j < 16; j++)
                {
                    // ���� ������� ������ ������ � ��������� � �������
                    if (source[i] == _HEX[j])
                    {
                        letterSearchRes = true;
                        letterIdx = j;

                        break;
                    }
                }

                // ���� ���� �� ���� ������ �� ��� ������� � �������,
                // �������� ��� ��������� ������ ������������� ����������
                if (letterSearchRes != true)
                {
                    return false;
                }

                // ���� ����� �� ������� ������� ����, ����� ���������������
                // ������� ������ � ���������� ������� ������� ���������� int16
                value += (letterIdx << shift);
            }

            return true;
        }

        #endregion Private Operations

        #region Public Properties

        /// <summary>
        /// ������������ ����� ����� ����� �� ������� ��������
        /// </summary>
        public int MaxFilenameLength
        {
            get
            {
                return _maxFilenameLength;
            }

            set
            {
                // ���� ��������������� �������� ������ ����� ��������, ���������
                // �������� �������� (�.�. ��� ������ ��������� ���� �� ���� ������)
                if (value > 14)
                {
                    _maxFilenameLength = value;
                }
            }
        }

        #endregion Public Properties

        #region Constants

        // ����� ��������, ������������ � ����� ��� "��������"
        private const int _prefixLength = 14; // "@VVVVNNNNMMMM."

        #endregion Constants

        #region Data

        // ������������ ����� ����� ����� �� ������� ��������
        private int _maxFilenameLength;

        // ������ �������� ������������������ �������������
        char[] _HEX;

        #endregion Data

    } // class FileNamer
}
