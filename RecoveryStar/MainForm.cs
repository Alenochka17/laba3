/*----------------------------------------------------------------------+
 |	filename:	MainForm.cs                                             |
 |----------------------------------------------------------------------|
 |	version:	1.14                                                    |
 |	revision:   01/03/2007	15:08                                       |
 |  author:		Дробанов Артём Федорович (DrAF)                         |
 |	e-mail:		draf@mail.ru                                            |
 |	purpose:	Работа с файлами                                        |
 +----------------------------------------------------------------------+
 |  Отказоустойчивое кодирование по типу RAID систем                    |
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

            // Инициализируем экземпляр класса для упаковки (распаковки) имени файла
            // в префиксный формат
            _eFileNamer = new FileNamer();
        }

        #endregion Construction & Destruction

        #region Private Operations

        /// <summary>
        /// Метод обработки файлов в выбранной директории
        /// </summary>
        private void ProcessFiles()
        {
            // Если в браузере в качестве текущего элемента выбрана директория
            if (browser.SelectedItem.IsFolder)
            {
                // Если в данный момент форма открыта - просто выходим
                if (
                        (_eProcessForm != null)
                     &&
                        (_eProcessForm.Visible)
                    )
                {
                    return;
                }

                // Устанавливаем параметры кодера
                _eProcessForm.DataCount = _dataCount;
                _eProcessForm.EccCount  = _eccCount;

                // Копируем имена файлов для обработки                                                
                for (int i = 0; i < browser.SelectedItem.SubFiles.Count; i++)
                {
                    // Получаем короткий вариант имени файла
                    String shortFilename = _eFileNamer.GetShortFilename(((ShellDll.ShellItem)browser.SelectedItem.SubFiles.Items[i]).Path);

                    // Если имя исходного файла превышает 50 символов - он не может быть обработан
                    if (shortFilename.Length > 50)
                    {
                        string message = "Длина имени файла \"" + shortFilename + "\" превышает 50 символов! Пропустить этот файл и продолжать процесс формирования списка для обработки?";
                        string caption = " Recovery Star 1.14";
                        MessageBoxButtons buttons = MessageBoxButtons.YesNo;
                        DialogResult result;
                        result = MessageBox.Show(null, message, caption, buttons, MessageBoxIcon.Exclamation, MessageBoxDefaultButton.Button1);

                        // Если пользователь нажал на кнопку "No" - прерываем обработку
                        if (result == DialogResult.No)
                        {
                            return;
                        }

                    } else
                    {
                        _eProcessForm.FilenamesToProcess.Add(((ShellDll.ShellItem)browser.SelectedItem.SubFiles.Items[i]).Path);
                    }
                }

                // Если размер списка для обработки не равен нулю
                // (т.е. есть что обрабатывать) - будем осуществлять обработку
                if (_eProcessForm.FilenamesToProcess.ToArray().Length != 0)
                {
                    // Выходим выше уровнем в директориях, чтобы не тормозить процесс обработки
                    browser.BrowserUp();

                    // Отображаем диалоговое окно обработки
                    _eProcessForm.Show();

                } else
                {
                    string message = "В указанной директории не найдено файлов для обработки!";
                    string caption = " Recovery Star 1.14";
                    MessageBoxButtons buttons = MessageBoxButtons.OK;
                    MessageBox.Show(null, message, caption, buttons, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1);
                }
            }
        }

        /// <summary>
        /// Метод обработки файлов в выбранной директории с учетом уникального имени,
        /// породившего последовательность
        /// </summary>
        private void ProcessUniqueFiles()
        {
            // Если в браузере в качестве текущего элемента выбрана директория
            if (browser.SelectedItem.IsFolder)
            {
                // Если в данный момент форма открыта - просто выходим
                if (
                        (_eProcessForm != null)
                     &&
                        (_eProcessForm.Visible)
                    )
                {
                    return;
                }

                // Список уникальных имен файлов для обработки
                ArrayList uniqueNamesToProcess = new ArrayList();

                // Копируем имена файлов для обработки                                                
                for (int i = 0; i < browser.SelectedItem.SubFiles.Count; i++)
                {
                    // Извлекаем очередное имя из списка...
                    String fullFilename = (String)((ShellDll.ShellItem)browser.SelectedItem.SubFiles.Items[i]).Path;

                    //...получаем его короткий вариант...
                    String shortFilename = _eFileNamer.GetShortFilename(fullFilename);

                    //...и распаковываем его с получением оригинального имени...
                    String unpackedFilename = shortFilename;

                    // Если не удалось корректно распаковать короткое имя - переходим
                    // на следующую итерацию
                    if (!_eFileNamer.Unpack(ref unpackedFilename))
                    {
                        continue;
                    }

                    //...затем проверяем его на уникальность - если такое имя уже есть
                    // в словаре "uniqueNamesToProcess", то добавлять его не будем

                    // Сначала предполагаем, что распакованное имя файла уникально
                    bool unpackedFilenameIsUnique = true;

                    // Перебираем весь имеющийся список уникальных имен
                    foreach (String currUnpackedFilename in uniqueNamesToProcess)
                    {
                        // Если обнаружили совпадение - имя не уникально,
                        // сообщаем об этом и выходим из списка
                        if (currUnpackedFilename == unpackedFilename)
                        {
                            unpackedFilenameIsUnique = false;

                            break;
                        }
                    }

                    // Если распакованный файл уникален...
                    if (unpackedFilenameIsUnique)
                    {
                        //...добавляем имя в список уникальных имен...
                        uniqueNamesToProcess.Add(unpackedFilename);

                        //...добавляем имя в список для обработки...
                        _eProcessForm.FilenamesToProcess.Add(fullFilename);
                    }
                }

                // Если размер списка для обработки не равен нулю
                // (т.е. есть что обрабатывать) - будем осуществлять обработку
                if (_eProcessForm.FilenamesToProcess.ToArray().Length != 0)
                {
                    // Выходим выше уровнем в директориях, чтобы не тормозить процесс обработки
                    browser.BrowserUp();

                    // Отображаем диалоговое окно обработки
                    _eProcessForm.Show();

                } else
                {
                    string message = "В указанной директории не найдено файлов для обработки!";
                    string caption = " Recovery Star 1.14";
                    MessageBoxButtons buttons = MessageBoxButtons.OK;
                    MessageBox.Show(null, message, caption, buttons, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1);
                }
            }
        }

        /// <summary>
        /// Запуск режима кодирования файла
        /// </summary>        
        private void protectButton_Click(object sender, EventArgs e)
        {
            // После клика на кнопке переносим фокус на файловый браузер
            browser.Focus();

            // Создаем форму кодирования файла
            _eProcessForm = new ProcessForm();

            // Закрываем форму, если была открыта в конце обработки
            if (_eProcessForm.Visible)
            {
                _eProcessForm.Close();
            }

            // Устанавливаем режим работы
            _eProcessForm.Mode = RSModes.Protect;

            // Запускаем обработку файла
            ProcessFiles();
        }

        /// <summary>
        /// Запуск режима восстановления данных файла
        /// </summary>        
        private void recoverButton_Click(object sender, EventArgs e)
        {
            // После клика на кнопке переносим фокус на файловый браузер
            browser.Focus();

            // Создаем форму кодирования файла
            _eProcessForm = new ProcessForm();

            // Закрываем форму, если была открыта в конце обработки
            if (_eProcessForm.Visible)
            {
                _eProcessForm.Close();
            }

            // Устанавливаем режим работы
            _eProcessForm.Mode = RSModes.Recover;

            // Запускаем обработку файла
            ProcessUniqueFiles();
        }

        /// <summary>
        /// Запуск режима лечения набора данных файла
        /// </summary>        
        private void repairButton_Click(object sender, EventArgs e)
        {
            // После клика на кнопке переносим фокус на файловый браузер
            browser.Focus();

            // Создаем форму кодирования файла
            _eProcessForm = new ProcessForm();

            // Закрываем форму, если была открыта в конце обработки
            if (_eProcessForm.Visible)
            {
                _eProcessForm.Close();
            }

            // Устанавливаем режим работы
            _eProcessForm.Mode = RSModes.Repair;

            // Запускаем обработку файла
            ProcessUniqueFiles();
        }

        /// <summary>
        /// Запуск режима тестирования набора данных файла
        /// </summary>        
        private void testButton_Click(object sender, EventArgs e)
        {
            // После клика на кнопке переносим фокус на файловый браузер
            browser.Focus();

            // Создаем форму кодирования файла
            _eProcessForm = new ProcessForm();

            // Закрываем форму, если была открыта в конце обработки
            if (_eProcessForm.Visible)
            {
                _eProcessForm.Close();
            }

            // Устанавливаем режим работы
            _eProcessForm.Mode = RSModes.Test;

            // Запускаем обработку файла
            ProcessUniqueFiles();
        }

        /// <summary>
        /// Метод установки конфигурации кодера с отображением соответствующих
        /// изменений на форме
        /// </summary>
        private void SetCoderConfig()
        {
            // Инициализируем массивы, хранящие константные значения размера тома и избыточности,
            // доступных пользователю
            _allVolCountTrackBarValuesArr = new int[] {4, 8, 16, 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384};
            _redundancyTrackBarValuesArr = new int[] {5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70, 75, 80, 85, 90, 95, 100};

            // Снимаем данные с элементов управления (общее количество томов и избыточность)
            _allVolCount = _allVolCountTrackBarValuesArr[allVolCountMacTrackBar.Value];
            _redundancy = _redundancyTrackBarValuesArr[redundancyMacTrackBar.Value];

            // Устанавливаем текстовые метки, соответствующие значениям элементов управления "TrackBar"
            allVolCountGroupBox.Text = " Общее количество томов: " + System.Convert.ToString(_allVolCount);
            redundancyGroupBox.Text = " Избыточность кодирования: " + System.Convert.ToString(_redundancy) + " %";

            // Абсолютное значение количества процентов на том
            double percByVol = (double)_allVolCount / (double)200;

            // Вычисляем количество томов для восстановления
            _eccCount = (int)((double)_redundancy * percByVol); // Томов для восстановления

            // В случае необходимости корректируем количество томов для восстановления
            if (_eccCount < 1)
            {
                _eccCount = 1;
            }

            // Количество основных томов находим по остаточному принципу
            _dataCount = _allVolCount - _eccCount;

            // Пока количество томов
            while (_eccCount >= _dataCount)
            {
                _eccCount--;
                _dataCount++;
            }

            // Обеспечиваем четное общее количество томов (повышает скорость обработки матриц
            // в кодере Рида-Соломона, т.к. матрицы перемножаются методом Винограда)
            // Но это действие разрешаем только тогда, когда есть резерв по перебалансировке!
            if (
                    (_eccCount > 1)
                 &&
                    ((_dataCount % 2) != 0)                
                )
            {
                _eccCount--;
                _dataCount++;
            }

            // Вычисляем результирующий объем
            String outX = System.Convert.ToString((((double)(_dataCount + _eccCount)) / (double)_dataCount));
            
            // Длина подстроки, выделяемой по-умолчанию
            int subStrLen = 6;
    
            // Корректируем (в случае надобности) длину извлекаемой подстроки
            if (outX.Length < 6)
            {
                subStrLen = outX.Length;
            }

            // Выводим конфигурацию кодера
            coderConfigGroupBox.Text = " Конфигурация кодера (основных томов: " + System.Convert.ToString(_dataCount)
                                     + "; томов для восстановления: " + System.Convert.ToString(_eccCount)
                                     + "; объем выхода: " + outX.Substring(0, subStrLen)
                                     + " X)";
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            // Устанавливаем общее количество томов по-умолчанию - 1024
            allVolCountMacTrackBar.Value = 8;

            // Устанавлиаем избыточность кодирования - 50%
            redundancyMacTrackBar.Value = 9;

            // Устанавливаем конфигурацию кодера
            SetCoderConfig();
        }

        private void allVolCountMacTrackBar_ValueChanged(object sender, decimal value)
        {
            // Устанавливаем конфигурацию кодера
            SetCoderConfig();
        }

        private void redundancyMacTrackBar_ValueChanged(object sender, decimal value)
        {
            // Устанавливаем конфигурацию кодера
            SetCoderConfig();
        }

        private void вызовСправкиToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // Открываем файл справки
            if (File.Exists("help.mht"))
            {
                System.Diagnostics.Process.Start("help.mht");
            }
        }

        private void оПрограммеToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AboutForm eAboutForm = new AboutForm();
            eAboutForm.ShowDialog();
        }

        private void выходToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }
        
        #endregion Private Operations

        #region Data

        // Объект для упаковки (распаковки) имени в префиксный формат
        private FileNamer _eFileNamer;

        // Массивы значений индексов для элементов управления "TrackBar" (количество томов и избыточность)
        private int[] _allVolCountTrackBarValuesArr;
        private int[] _redundancyTrackBarValuesArr;

        // Общее количество томов
        private int _allVolCount;

        // Избыточность кодирования
        private int _redundancy;
        
        // Количество основных томов
        private int _dataCount;

        // Количество томов для восстановления
        private int _eccCount;

        // Форма обработки файлов
        ProcessForm _eProcessForm;

        #endregion Data                        
    }
}