/*----------------------------------------------------------------------+
 |	filename:	FileSplitter.cs                                         |
 |----------------------------------------------------------------------|
 |	version:	1.14                                                    |
 |	revision:   02/03/2007	08:45                                       |
 |  author:		Дробанов Артём Федорович (DrAF)                         |
 |	e-mail:		draf@mail.ru                                            |
 |	purpose:	Работа с файлами                                        |
 +----------------------------------------------------------------------+
 |  Разрезание (склеивание) файлов-томов на фрагменты                   |
 +----------------------------------------------------------------------*/

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.IO;

namespace RecoveryStar
{
    /// <summary>
    /// Делегат обновления прогресса разрезания (склеивания) файла
    /// </summary>
    /// <param name="progress">Текущий прогресс</param>
    public delegate void OnUpdateFileSplittingProgressHandler(double progress);

    /// <summary>
    /// Делегат завершения процесса разрезания (склеивания) файла
    /// </summary>
    public delegate void OnFileSplittingFinishHandler();

    /// <summary>
    /// Класс для разрезания (склеивания) файлов на фрагменты
    /// </summary>
    public class FileSplitter
    {
        #region Construction & Destruction

        public FileSplitter()
        {            
            // Создаем экземпляр класса для формирования имени тома            
            _eFileNamer = new FileNamer();

            // Путь к файлам для обработки по-умолчанию пустой
            _path = "";

            // Инициализируем имя файла по-умолчанию            
            _filename = "NONAME";

            // Размер файлового буфера (по-умолчанию) - 64 Мб
            _bufferLength = (64 * 1024 * 1024);

            // Выделяем память под файловый буфер
            _buffer = new byte[_bufferLength];

            // В переменной Int64 содержится 8 байт
            _dataLengthArr = new byte[8];

            // Экземляр класса полностью закончил обработку?
            _finished = true;

            // Обработка произведена корректно?
            _processedOK = false;

            // По-умолчанию устанавливается фоновый приоритет
            _threadPriority = 0;

            // Инициализируем событие прекращения обработки файлов
            _exitEvent = new ManualResetEvent[] { new ManualResetEvent(false) };

            // Инициализируем cобытие продолжения обработки файлов
            _executeEvent = new ManualResetEvent[] { new ManualResetEvent(false) };

            // Событие, устанавливаемое по завершении обработки
            _finishedEvent = new ManualResetEvent[] { new ManualResetEvent(true) };
        }

        #endregion Construction & Destruction

        #region Delegates

        /// <summary>
        /// Делегат обновления прогресса разбиения (склеивания) файла
        /// </summary>        
        public OnUpdateFileSplittingProgressHandler OnUpdateFileSplittingProgress;

        /// <summary>
        /// Делегат завершения процесса разбиения (склеивания) файла
        /// </summary>
        public OnFileSplittingFinishHandler OnFileSplittingFinish;

        #endregion Delegates

        #region Public Operations
        
        /// <summary>
        /// Разбиение исходного файла на фрагменты (тома)
        /// </summary>
        /// <param name="path">Путь к файлам для обработки</param>
        /// <param name="filename">Имя файла для разбиения</param>        
        /// <param name="dataCount">Конфигурация количества основных томов</param>
        /// <param name="eccCount">Конфигурация количества томов для восстановления</param>
        /// <param name="runAsSeparateThread">Запускать в отдельном потоке?</param>
        /// <returns>Булевский флаг операции</returns>
        public bool StartToSplit(String path, String filename, int dataCount, int eccCount, bool runAsSeparateThread)
        {
            // Если поток разбиения файла на фрагменты работает - не позволяем повторный запуск        
            if (InProcessing)
            {
                return false;
            }

            // Сбрасываем флаг корректности результата перед запуском потока
            _processedOK = false;

            // Сбрасываем индикатор актуального состояния переменных-членов
            _finished = false;
            
            // Сохраняем путь к файлам для обработки
            if (path == null)
            {
                _path = "";

            } else
            {
                // Производим выделение пути из "_path" в случае,
                // если туда было записано полное имя
                _path = _eFileNamer.GetPath(path);
            }

            if (filename == null)
            {
                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return false;
            }
                      
            // Производим выделение короткого имени файла из "_filename" в случае,
            // если туда был записано полное имя
            _filename = _eFileNamer.GetShortFilename(filename);
           
            // Проверяем на корректную конфигурацию
            if (
                    (eccCount > dataCount)
                 ||
                    (eccCount <= 0)
                 ||
                    ((dataCount + eccCount) > 65535)
                )
            {
                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return false;
            }
           
            // Сохраняем количество основных томов
            _dataCount = dataCount;

            // Сохраняем количество томов для восстановления
            _eccCount = eccCount;

            // Указываем, что поток должен выполняться
            _exitEvent[0].Reset();
            _executeEvent[0].Set();            
            _finishedEvent[0].Reset();

            // Если указано, что не требуется запуск в отдельном потоке,
            // запускаем в данном
            if (!runAsSeparateThread)
            {
                // Разбиваем исходный файл на фрагменты
                Split();

                // Возвращаем результат обработки
                return _processedOK;
            }

            // Создаем поток разбиения файла на фрагменты...
            _thrFileVolProc = new Thread(new ThreadStart(Split));

            //...затем даем ему имя...
            _thrFileVolProc.Name = "FileSplitter.Split()";
      
            //...устанавливаем выбранный приоритет задачи...
            _thrFileVolProc.Priority = _threadPriority;
           
            //...и запускаем его
            _thrFileVolProc.Start();

            // Сообщаем, что все нормально
            return true;           
        }

        /// <summary>
        /// Склеивание файла из фрагментов
        /// </summary>
        /// <param name="path">Путь к файлам для обработки</param>
        /// <param name="filename">Имя файла одного из основных томов</param>        
        /// <param name="dataCount">Конфигурация количества основных томов</param>
        /// <param name="eccCount">Конфигурация количества томов для восстановления</param>
        /// <param name="runAsSeparateThread">Запускать в отдельном потоке?</param>
        /// <returns>Булевский флаг операции</returns>
        public bool StartToGlue(String path, String filename, int dataCount, int eccCount, bool runAsSeparateThread)
        {
            // Если поток склеивания файла из фрагментов работает - не позволяем повторный запуск
            if (InProcessing)
            {
                return false;
            }

            // Сбрасываем флаг корректности результата перед запуском потока
            _processedOK = false;

            // Сбрасываем индикатор актуального состояния переменных-членов
            _finished = false;

            // Сохраняем путь к файлам для обработки
            if (path == null)
            {
                _path = "";

            } else
            {
                // Производим выделение пути из "_path" в случае,
                // если туда было записано полное имя
                _path = _eFileNamer.GetPath(path);
            }

            if (filename == null)
            {
                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return false;
            }
                        
            // Производим выделение короткого имени файла из "_filename" в случае,
            // если туда было записано полное имя
            _filename = _eFileNamer.GetShortFilename(filename);
           
            // Проверяем на корректную конфигурацию
            if (
                    (eccCount > dataCount)
                 ||
                    (eccCount <= 0)
                 ||
                    ((dataCount + eccCount) > 65535)
                )
            {
                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return false;
            }

            // Сохраняем количество основных томов
            _dataCount = dataCount;

            // Сохраняем количество томов для восстановления
            _eccCount = eccCount;

            // Указываем, что поток должен выполняться
            _exitEvent[0].Reset();
            _executeEvent[0].Set();            
            _finishedEvent[0].Reset();

            // Если указано, что не требуется запуск в отдельном потоке,
            // запускаем в данном
            if (!runAsSeparateThread)
            {
                // Выполняем "склеивание" файлов из фрагментов в исходный
                Glue();

                // Возвращаем результат обработки
                return _processedOK;
            }

            // Создаем поток склеивания файлов из фрагментов...
            _thrFileVolProc = new Thread(new ThreadStart(Glue));

            //...затем даем ему имя...
            _thrFileVolProc.Name = "FileSplitter.Glue()";
    
            //...устанавливаем выбранный приоритет задачи...
            _thrFileVolProc.Priority = _threadPriority;
            
            //...и запускаем его
            _thrFileVolProc.Start();

            // Сообщаем, что все нормально
            return true;
        }

        /// <summary>
        /// Метод остановки потока
        /// </summary>
        public void Stop()
        {
            // Указываем, что поток обработки больше не должен выполняться
            _executeEvent[0].Set(); // Это необходимо для принудительного снятия с паузы
            _exitEvent[0].Set();
        }

        /// <summary>
        /// Постановка потока обработки на паузу
        /// </summary>
        public void Pause()
        {
            // Ставим обработку на паузу
            _executeEvent[0].Reset();
        }

        /// <summary>
        /// Снятие потока обработки с паузы
        /// </summary>
        public void Continue()
        {
            // Снимаем обработку c паузы
            _executeEvent[0].Set();
        }

        #endregion Public Operations

        #region Private Operations

        /// <summary>
        /// Разбиение файла на фрагменты
        /// </summary>
        private void Split()
        {
            // Экземпляры файловых потоков (исходный и целевой)
            FileStream fileStreamSource = null;
            FileStream fileStreamTarget = null;
            
            // Экземпляр класса записи обычных типов данных в двоичный файл
            BinaryWriter eBinaryWriter = null;
            
            try
            {
                // Имя файла для обработки
                String filename;

                // Формируем полное имя файла
                filename = _path + _filename;

                // Открываем поток исходного файла на чтение
                fileStreamSource = new FileStream(filename, FileMode.Open, System.IO.FileAccess.Read);

                // Вычисляем длину основного тома
                long volumeLength = (fileStreamSource.Length / _dataCount);

                // Сохраняем счетчик недозаписанных байт (из исходного потока в целевой)
                Int64 unwrittenCounter = fileStreamSource.Length;

                // Если при выбранной длине тома все данные не вместятся в набор томов,
                // добавляем ещё по одному байту к каждому тому. При этом в части томов этот
                // байт не будет израсходован
                if ((fileStreamSource.Length % _dataCount) != 0)
                {
                    volumeLength++;
                }

                // Одним из требований RAID-подобного кодера Рида-Соломона является четный размер
                // входа. Обеспечиваем его.
                if ((volumeLength % 2) != 0)
                {
                    volumeLength++;
                }
                                                
                // Вычисляем значение модуля, который позволит выводить процент обработки
                // ровно при единичном приращении для цикла по "volNum"
                int progressMod1 = (_dataCount / 100);

                // Если модуль равен нулю, то увеличиваем его до значения "1", чтобы
                // прогресс выводился на каждой итерации (файл очень маленький)
                if (progressMod1 == 0)
                {
                    progressMod1 = 1;
                }

                // Вычисляем значение модуля, который позволит выводить процент обработки
                // ровно при единичном приращении для цикла по "i" внутри цикла по "volNum"
                int progressMod2 = (int)((fileStreamSource.Length / _bufferLength) / 100);

                // Если модуль равен нулю, то увеличиваем его до значения "1", чтобы
                // прогресс выводился на каждой итерации (файл очень маленький)
                if (progressMod2 == 0)
                {
                    progressMod2 = 1;
                }
                
                // Cчетчик количества записанных байт в выходной поток
                Int64 volumeWriteCounter = 0;

                // Работаем со всеми основными томами (+1 фиктивная итерация для сброса буферов)
                for (int volNum = 0; volNum <= _dataCount; volNum++)
                {
                    // Если мы находимся не на первой итерации, то требуется сбросить
                    // файловый буфер выходного потока с дозаписью значения реально
                    // содержащихся данных
                    if (volNum != 0)
                    {
                        // Используем имеющийся открытый файловый поток для инициализации
                        // экземпляра класса записи обычных типов данных в двоичный файл
                        eBinaryWriter = new BinaryWriter(fileStreamTarget);

                        if (eBinaryWriter != null)
                        {
                            // Перемещаемся на конец файла...
                            eBinaryWriter.Seek(0, SeekOrigin.End);

                            //...и пишем в его конец длину блока полезных данных...                        
                            eBinaryWriter.Write(volumeWriteCounter);

                            //...и, затем, обнуляем числовое значение
                            volumeWriteCounter = 0;

                            // Сбрасываем буфер "BinaryWriter"
                            eBinaryWriter.Flush();

                            // Закрываем
                            eBinaryWriter.Close();
                            eBinaryWriter = null;
                        }
                        
                        if (fileStreamTarget != null)
                        {            
                            //...и закрываем файловый поток
                            fileStreamTarget.Close();
                            fileStreamTarget = null;
                        }
                    }

                    // Если данное условие выполнится - мы находимся на фиктивной итерации,
                    // и требуется выход из цикла (т.к. все тома уже обработаны)
                    if (volNum == _dataCount)
                    {
                        if (fileStreamSource != null)
                        {
                            // Перед выходом закрываем поток исходного файла
                            fileStreamSource.Close();
                            fileStreamSource = null;
                        }

                        // Сообщаем, что обработка файла закончена
                        if (OnFileSplittingFinish != null)
                        {
                            OnFileSplittingFinish();
                        }

                        break;
                    }

                    // Считываем первоначальное имя файла
                    filename = _filename;
                    
                    // Упаковываем исходное имя файла в префиксный формат
                    // (для перебора всех томов в цикле)
                    if (!_eFileNamer.Pack(ref filename, volNum, _dataCount, _eccCount))
                    {
                        // Закрываем исходный и целевой файловые потоки
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

                        // Указываем на то, что произошла ошибка работы с файлами
                        _processedOK = false;

                        // Активируем индикатор актуального состояния переменных-членов
                        _finished = true;

                        // Устанавливаем событие завершения обработки
                        _finishedEvent[0].Set();

                        return;
                    }
                   
                    // Формируем полное имя файла
                    filename = _path + filename;

                    // Открываем поток целевого файла на запись
                    fileStreamTarget = new FileStream(filename, FileMode.Create, System.IO.FileAccess.Write);

                    // Если есть что записать
                    if (unwrittenCounter > 0)
                    {
                        // Количество основных итераций (копирование полным буфером)
                        Int64 nIterations;

                        // Остаток, не подпадающий под вычисления основных итераций
                        int iterRest;

                        // Если счетчик недозаписанных байт больше либо равен размеру тома -
                        // нужно применять обычное копирование с последующей дозаписью значения
                        // размера тома и переходом на новую итерацию
                        if (unwrittenCounter >= volumeLength)
                        {
                            // Узнаем количество основных итераций (копирование полным буфером)
                            nIterations = volumeLength / _bufferLength;

                            // Вычисляем остаток, не подпадающий под вычисления основных итераций
                            iterRest = (int)(volumeLength - (nIterations * _bufferLength));

                        } else
                        {
                            // Узнаем количество основных итераций (копирование полным буфером)
                            nIterations = (unwrittenCounter / _bufferLength);

                            // Вычисляем остаток, не подпадающий под вычисления основных итераций
                            iterRest = (int)(unwrittenCounter - (nIterations * _bufferLength));

                            // Расширяем размер файлового потока до "volumeLength"
                            fileStreamTarget.SetLength(volumeLength);
                        }

                        // Работа с полноразмерными буферами (работа в основных итерациях)
                        for (Int64 i = 0; i < nIterations; i++)
                        {
                            // Читаем данные в буфер
                            fileStreamSource.Read(_buffer, 0, _bufferLength);

                            // Пишем данные из буфера
                            fileStreamTarget.Write(_buffer, 0, _bufferLength);
                            volumeWriteCounter += _bufferLength;
                            unwrittenCounter   -= _bufferLength;

                            // Выводим прогресс обработки
                            if (
                                    ((((volNum * nIterations) + i) % progressMod2) == 0)
                                 &&
                                    (OnUpdateFileSplittingProgress != null)
                                )
                            {
                                OnUpdateFileSplittingProgress(((double)((volNum * nIterations) + (i + 1)) / (double)(_dataCount * nIterations)) * 100.0);
                            }

                            // В случае, если требуется постановка на паузу, событие "_executeEvent"
                            // будет сброшено, и будем на паузе вплоть до его появления
                            ManualResetEvent.WaitAll(_executeEvent);

                            // Если указано, что требуется выйти из потока - выходим
                            if (ManualResetEvent.WaitAll(_exitEvent, 0, false))
                            {
                                // Закрываем исходный и целевой файловые потоки
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

                                // Указываем на то, что обработка была прервана
                                _processedOK = false;

                                // Активируем индикатор актуального состояния переменных-членов
                                _finished = true;

                                // Устанавливаем событие завершения обработки
                                _finishedEvent[0].Set();

                                return;
                            }
                        }

                        // Дорабатываем остаток (если он есть)
                        if (iterRest > 0)
                        {
                            // Читаем данные в буфер
                            fileStreamSource.Read(_buffer, 0, iterRest);

                            // Пишем данные из буфера
                            fileStreamTarget.Write(_buffer, 0, iterRest);
                            volumeWriteCounter += iterRest;
                            unwrittenCounter   -= iterRest;

                            // Выводим прогресс обработки
                            if (
                                    ((volNum % progressMod1) == 0)
                                 &&
                                    (OnUpdateFileSplittingProgress != null)
                                )
                            {
                                OnUpdateFileSplittingProgress(((double)(volNum + 1) / (double)_dataCount) * 100.0);
                            }

                            // В случае, если требуется постановка на паузу, событие "_executeEvent"
                            // будет сброшено, и будем на паузе вплоть до его появления
                            ManualResetEvent.WaitAll(_executeEvent);

                            // Если указано, что требуется выйти из потока - выходим
                            if (ManualResetEvent.WaitAll(_exitEvent, 0, false))
                            {
                                // Закрываем исходный и целевой файловые потоки
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

                                // Указываем на то, что обработка была прервана
                                _processedOK = false;

                                // Активируем индикатор актуального состояния переменных-членов
                                _finished = true;

                                // Устанавливаем событие завершения обработки
                                _finishedEvent[0].Set();

                                return;
                            }
                        }

                        continue;
                    }

                    // Если в исходном потоке данных больше нет, нужно просто заполнить
                    // весь том нулями, не забыв про дополнительные 8 нулей переменной,
                    // указывающей на количество оригинальных данных
                    if (unwrittenCounter == 0)
                    {
                        // Расширяем размер файлового потока до "volumeLength"
                        fileStreamTarget.SetLength(volumeLength);

                        // Выводим прогресс обработки                     
                        if (
                                ((volNum % progressMod1) == 0)
                             &&
                                (OnUpdateFileSplittingProgress != null)
                            )
                        {
                            OnUpdateFileSplittingProgress(((double)(volNum + 1) / (double)_dataCount) * 100.0);
                        }

                        // В случае, если требуется постановка на паузу, событие "_executeEvent"
                        // будет сброшено, и будем на паузе вплоть до его появления
                        ManualResetEvent.WaitAll(_executeEvent);

                        // Если указано, что требуется выйти из потока - выходим
                        if (ManualResetEvent.WaitAll(_exitEvent, 0, false))
                        {
                            // Закрываем исходный и целевой файловые потоки
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

                            // Указываем на то, что обработка была прервана
                            _processedOK = false;

                            // Активируем индикатор актуального состояния переменных-членов
                            _finished = true;

                            // Устанавливаем событие завершения обработки
                            _finishedEvent[0].Set();

                            return;
                        }

                        continue;
                    }
                }
            }

            // Если было хотя бы одно исключение - закрываем файловые потоки и
            // сообщаем об ошибке
            catch (IOException e)
            {
                // Закрываем исходный и целевой файловые потоки
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

                // Указываем на то, что произошла ошибка работы с файлами
                _processedOK = false;

                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return;
            }

            // Указываем на то, что обработка была произведена корректно
            _processedOK = true;

            // Активируем индикатор актуального состояния переменных-членов
            _finished = true;

            // Устанавливаем событие завершения обработки
            _finishedEvent[0].Set();
        }

        /// <summary>
        /// Склеивание файлов из фрагментов
        /// </summary>
        private void Glue()
        {
            // Экземпляры файловых потоков (исходный и целевой)
            FileStream fileStreamSource = null;
            FileStream fileStreamTarget = null;

            // Номер текущего тома
            int volNum;

            // Имя файла для обработки
            String filename;

            try
            {
                // Формируем полное имя файла
                filename = _path + _filename;

                // Открываем поток целевого файла на запись
                fileStreamTarget = new FileStream(filename, FileMode.Create, System.IO.FileAccess.Write);
                
                // Вычисляем значение модуля, который позволит выводить процент обработки
                // ровно при единичном приращении для цикла по "volNum"
                int progressMod1 = (_dataCount / 100);

                // Если модуль равен нулю, то увеличиваем его до значения "1", чтобы
                // прогресс выводился на каждой итерации (файл очень маленький)
                if (progressMod1 == 0)
                {
                    progressMod1 = 1;
                }

                // Считываем первоначальное имя файла
                filename = _filename;

                // Упаковываем исходное имя файла в префиксный формат для получения имени первого тома           
                _eFileNamer.Pack(ref filename, 0, _dataCount, _eccCount);

                // Формируем полное имя файла
                filename = _path + filename;

                // Открываем поток исходного файла на чтение...
                fileStreamSource = new FileStream(filename, FileMode.Open, System.IO.FileAccess.Read);

                // Вычисляем значение модуля, который позволит выводить процент обработки
                // ровно при единичном приращении для цикла по "i" внутри цикла по "volNum"
                int progressMod2 = (int)(((fileStreamSource.Length - 8) / _bufferLength) / 100);

                // Если модуль равен нулю, то увеличиваем его до значения "1", чтобы
                // прогресс выводился на каждой итерации (файл очень маленький)
                if (progressMod2 == 0)
                {
                    progressMod2 = 1;
                }

                // Закрываем поток исходного файла
                if (fileStreamSource != null)
                {
                    fileStreamSource.Close();
                    fileStreamSource = null;
                }
                
                // Работаем со всеми основными томами
                for (volNum = 0; volNum < _dataCount; volNum++)
                {
                    // Считываем первоначальное имя файла
                    filename = _filename;

                    // Упаковываем исходное имя файла в префиксный формат
                    // (для перебора всех томов в цикле)
                    _eFileNamer.Pack(ref filename, volNum, _dataCount, _eccCount);
                    
                    // Формируем полное имя файла
                    filename = _path + filename;

                    // Если исходный файл не существует, сообщаем об ошибке
                    if (!File.Exists(filename))
                    {
                        // Указываем на то, что обработка была прервана
                        _processedOK = false;

                        // Активируем индикатор актуального состояния переменных-членов
                        _finished = true;

                        // Устанавливаем событие завершения обработки
                        _finishedEvent[0].Set();

                        return;
                    }

                    // Открываем поток исходного файла на чтение...
                    fileStreamSource = new FileStream(filename, FileMode.Open, System.IO.FileAccess.Read);

                    //...и выполняем позиционирование на конец файла, чтобы считать
                    // количество полезных байт в данном томе
                    fileStreamSource.Seek(((Int64)fileStreamSource.Length - (8 + 8)), SeekOrigin.Begin);

                    // Читаем сохраненное в конце файла значение CRC-64...
                    fileStreamSource.Read(_dataLengthArr, 0, 8);

                    // Устанавливаем курсор в файле на начало
                    fileStreamSource.Seek(0, SeekOrigin.Begin);

                    // Сохраненное в файле значение количества полезных байт в данном томе
                    UInt64 dataLength;

                    // Теперь преобразуем массив byte[] в Int64
                    dataLength  = 0x0000000000000000;
                    dataLength |= ((UInt64)_dataLengthArr[0] << 0)  & (0x00000000000000FF);
                    dataLength |= ((UInt64)_dataLengthArr[1] << 8)  & (0x000000000000FF00);
                    dataLength |= ((UInt64)_dataLengthArr[2] << 16) & (0x0000000000FF0000);
                    dataLength |= ((UInt64)_dataLengthArr[3] << 24) & (0x00000000FF000000);
                    dataLength |= ((UInt64)_dataLengthArr[4] << 32) & (0x000000FF00000000);
                    dataLength |= ((UInt64)_dataLengthArr[5] << 40) & (0x0000FF0000000000);
                    dataLength |= ((UInt64)_dataLengthArr[6] << 48) & (0x00FF000000000000);
                    dataLength |= ((UInt64)_dataLengthArr[7] << 56) & (0xFF00000000000000);

                    // Теперь, когда мы знаем количество полезных байт в данном томе, их все нужно
                    // записать в целевой файл
                    // Узнаем количество основных итераций (копирование полным буфером)
                    Int64 nIterations = (Int64)(dataLength / (UInt64)_bufferLength);

                    // Вычисляем остаток, не подпадающий под вычисления основных итераций
                    int iterRest = (int)((Int64)dataLength - (nIterations * _bufferLength));

                    // Работа с полноразмерными буферами (работа в основных итерациях)
                    for (Int64 i = 0; i < nIterations; i++)
                    {
                        // Читаем данные в буфер
                        fileStreamSource.Read(_buffer, 0, _bufferLength);

                        // Пишем данные из буфера
                        fileStreamTarget.Write(_buffer, 0, _bufferLength);

                        // Выводим прогресс обработки
                        if (
                                ((((volNum * nIterations) + i) % progressMod2) == 0)
                             &&
                                (OnUpdateFileSplittingProgress != null)
                            )
                        {
                            OnUpdateFileSplittingProgress(((double)((volNum * nIterations) + (i + 1)) / (double)(_dataCount * nIterations)) * 100.0);
                        }

                        // В случае, если требуется постановка на паузу, событие "_executeEvent"
                        // будет сброшено, и будем на паузе вплоть до его появления
                        ManualResetEvent.WaitAll(_executeEvent);

                        // Если указано, что требуется выйти из потока - выходим
                        if (ManualResetEvent.WaitAll(_exitEvent, 0, false))
                        {
                            // Закрываем исходный и целевой файловые потоки
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

                            // Указываем на то, что обработка была прервана
                            _processedOK = false;

                            // Активируем индикатор актуального состояния переменных-членов
                            _finished = true;

                            // Устанавливаем событие завершения обработки
                            _finishedEvent[0].Set();

                            return;
                        }
                    }

                    // Дорабатываем остаток (если он есть)
                    if (iterRest > 0)
                    {
                        // Читаем данные в буфер
                        fileStreamSource.Read(_buffer, 0, iterRest);

                        // Пишем данные из буфера
                        fileStreamTarget.Write(_buffer, 0, iterRest);
            
                        // Выводим прогресс обработки                     
                        if (
                                ((volNum % progressMod1) == 0)
                             &&
                                (OnUpdateFileSplittingProgress != null)
                            )
                        {
                            OnUpdateFileSplittingProgress(((double)(volNum + 1) / (double)_dataCount) * 100.0);
                        }

                        // В случае, если требуется постановка на паузу, событие "_executeEvent"
                        // будет сброшено, и будем на паузе вплоть до его появления
                        ManualResetEvent.WaitAll(_executeEvent);

                        // Если указано, что требуется выйти из потока - выходим
                        if (ManualResetEvent.WaitAll(_exitEvent, 0, false))
                        {
                            // Закрываем исходный и целевой файловые потоки
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

                            // Указываем на то, что обработка была прервана
                            _processedOK = false;

                            // Активируем индикатор актуального состояния переменных-членов
                            _finished = true;

                            // Устанавливаем событие завершения обработки
                            _finishedEvent[0].Set();

                            return;
                        }
                    }
                                        
                    // Закрываем файл исходного тома                    
                    if (fileStreamSource != null)
                    {
                        fileStreamSource.Close();
                        fileStreamSource = null;
                    }
                }
                
                // Сообщаем, что обработка файла закончена
                if (OnFileSplittingFinish != null)
                {
                    OnFileSplittingFinish();
                }
            }

            // Если было хотя бы одно исключение - закрываем файловые потоки и
            // сообщаем об ошибке
            catch (IOException e)
            {
                // Закрываем исходный и целевой файловые потоки
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
               
                // Указываем на то, что произошла ошибка работы с файлами
                _processedOK = false;

                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return;
            }

            // Закрываем целевой файл                        
            if (fileStreamTarget != null)
            {
                fileStreamTarget.Flush();
                fileStreamTarget.Close();
                fileStreamTarget = null;
            }
            
            // Указываем на то, что обработка была произведена корректно
            _processedOK = true;

            // Активируем индикатор актуального состояния переменных-членов
            _finished = true;

            // Устанавливаем событие завершения обработки
            _finishedEvent[0].Set();
        }

        #endregion Private Operations

        #region Public Properties

        /// <summary>
        /// Булевское свойство "Файл обрабатывается?"
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
        /// Булевское свойство "Экземпляр класса закончил обработку
        /// (имеет актуальное состояние переменных-членов)?"
        /// </summary>
        public bool Finished
        {
            get
            {
                // Если класс не занят обработкой - возвращаем значение
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
        /// Обработка файлов произведена корректно?"
        /// </summary>
        public bool ProcessedOK
        {
            get
            {
                // Если класс не занят обработкой - возвращаем значение
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
        /// Размер Файлового буфера
        /// </summary>
        public int BufferLength
        {
            get
            {
                // Если класс не занят обработкой - возвращаем значение...
                if (!InProcessing)
                {
                    return _bufferLength;

                } else
                {
                    //...а иначе сообщаем об обратном
                    return -1;
                }
            }

            set
            {
                // Если класс не занят обработкой - устанавливаем значение...
                if (!InProcessing)
                {
                    //... но только если оно не нарушает минимальный размер буфера - 8 Кб
                    if (value >= (8 * 1024))
                    {
                        _bufferLength = value;
                    }
                }
            }
        }

        /// <summary>
        /// Приоритет процесса
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

                    // Устанавливаем выбранный приоритет процесса                    
                    _thrFileVolProc.Priority = _threadPriority;                    
                }
            }
        }

        /// <summary>
        /// Событие, устанавливаемое по завершении обработки
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

        // Экземпляр класса для формирования имени тома
        private FileNamer _eFileNamer;

        // Путь к файлам для обработки
        private String _path;

        // Имя файла
        private String _filename;
            
        // Количество основных томов
        private int _dataCount;

        // Количество томов для восстановления
        private int _eccCount;
                               
        // Файловый буфер        
        private byte[] _buffer;

        // Размер файлового буфера (64 Мб)
        private int _bufferLength;

        // Буфер для преобразования байтового представления размера полезных данных
        private byte[] _dataLengthArr;

        // Экземляр класса полностью закончил обработку?
        private bool _finished;

        // Обработка произведена корректно?
        private bool _processedOK;
        
        // Поток разбиения (склеивания) файла на фрагменты
        private Thread _thrFileVolProc;

        // Приоритет процесса разбиения (склеивания) файла
        private ThreadPriority _threadPriority;

        // Событие прекращения обработки файлов
        private ManualResetEvent[] _exitEvent;

        // Событие продолжения обработки файлов
        private ManualResetEvent[] _executeEvent;

        // Событие, устанавливаемое по завершении обработки
        private ManualResetEvent[] _finishedEvent;
                
        #endregion Data

    } // class FileSplitter
}
