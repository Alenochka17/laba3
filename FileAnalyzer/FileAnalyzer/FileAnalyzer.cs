/*----------------------------------------------------------------------+
 |	filename:	FileAnalyzer.cs                                         |
 |----------------------------------------------------------------------|
 |	version:	1.14                                                    |
 |	revision:   02/03/2007	08:42                                       |
 |  author:		Дробанов Артём Федорович (DrAF)                         |
 |	e-mail:		draf@mail.ru                                            |
 |	purpose:	Контроль целостности данных                             |
 +----------------------------------------------------------------------+
 |  Реализация контроля целостности набора файлов-томов (CRC-64)        |
 +----------------------------------------------------------------------*/

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.IO;

namespace RecoveryStar
{
    /// <summary>
    /// Делегат обновления прогресса контроля целостности файлов
    /// </summary>
    /// <param name="progress">Текущий прогресс</param>
    public delegate void OnUpdateFileAnalyzeProgressHandler(double progress);

    /// <summary>
    /// Делегат завершения процесса контроля целостности файлов
    /// </summary>
    public delegate void OnFileAnalyzeFinishHandler();

    /// <summary>
    /// Делегат получения статистики повреждений многотомного архива
    /// </summary>
    /// <param name="percOfDamage">Процент повреждений по отношению к общему числу томов</param>
    /// <param name="percOfAltEcc">Процент запаса (по отношению к общему числу) по томам для восстановления</param>
    public delegate void OnGetDamageStatHandler(double percOfDamage, double percOfAltEcc);

    /// <summary>
    /// Класс контроля целостности набора файлов-томов
    /// </summary>
    public class FileAnalyzer
    {        
        #region Construction & Destruction

        /// <summary>
        /// Конструктор класса проверки целостности набора файлов
        /// </summary>
        public FileAnalyzer()
        {
            // Модуль для упаковки (распаковки) имени файла в префиксный формат
            _eFileNamer = new FileNamer();

            // Создаем экземпляр класса контроля целостности набора файлов
            _eFileIntegrityCheck = new FileIntegrityCheck();
        
            // Путь к файлам для обработки по-умолчанию пустой
            _path = "";
            
            // Инициализируем имя файла по-умолчанию            
            _filename = "NONAME";

            // Изначально все тома для восстановления считаем поврежденными
            _allEccVolsOK = false;
            
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

            // Инициализируем cобытие "пробуждения" цикла ожидания
            _wakeUpEvent = new ManualResetEvent[] { new ManualResetEvent(false) };

            // Событие, устанавливаемое по завершении обработки
            _finishedEvent = new ManualResetEvent[] { new ManualResetEvent(true) };
        }

        #endregion Construction & Destruction

        #region Delegates

        /// <summary>
        /// Делегат обновления прогресса контроля целостности файлов
        /// </summary>
        public OnUpdateFileAnalyzeProgressHandler OnUpdateFileAnalyzeProgress;

        /// <summary>
        /// Делегат завершения процесса контроля целостности файлов
        /// </summary>
        public OnFileAnalyzeFinishHandler OnFileAnalyzeFinish;

        /// <summary>
        /// Делегат получения статистики повреждений многотомного архива
        /// </summary>        
        public OnGetDamageStatHandler OnGetDamageStat;

        #endregion Delegates

        #region Public Operations

        /// <summary>
        /// Метод запуска потока обработки вычисления и записи CRC64 в конец файлов
        /// </summary>
        /// <param name="path">Путь к файлам для обработки</param>
        /// <param name="filename">Имя файла для обработки</param>        
        /// <param name="dataCount">Конфигурация количества основных томов</param>
        /// <param name="eccCount">Конфигурация количества томов для восстановления</param>
        /// <param name="runAsSeparateThread">Запускать в отдельном потоке?</param>
        /// <returns>Булевский флаг операции</returns>
        public bool StartToWriteCRC64(String path, String filename, int dataCount, int eccCount, bool runAsSeparateThread)
        {
            // Если поток вычисления CRC-64 работает - не позволяем повторный запуск        
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
                // Вычисляем CRC-64 для каждого из файлов набора
                WriteCRC64();

                // Возвращаем результат обработки
                return _processedOK;
            }
                        
            // Создаем поток вычисления и записи CRC-64...
            _thrFileAnalyze = new Thread(new ThreadStart(WriteCRC64));

            //...затем даем ему имя...
            _thrFileAnalyze.Name = "FileAnalyzer.WriteCRC64()";
                            
            //...устанавливаем выбранный приоритет задачи...
            _thrFileAnalyze.Priority = _threadPriority;
                     
            //...и запускаем его
            _thrFileAnalyze.Start();
  
            // Сообщаем, что все нормально
            return true;
        }

        /// <summary>
        /// Метод запуска потока обработки проверки CRC64, записанного в конец
        /// каждого из файлов набора, с генерированием списка имеющихся томов "volList",
        /// который будет использован декодером для восстановления данных
        /// </summary>
        /// <param name="path">Путь к файлам для обработки</param>
        /// <param name="filename">Имя файла для обработки</param>        
        /// <param name="dataCount">Конфигурация количества основных томов</param>
        /// <param name="eccCount">Конфигурация количества томов для восстановления</param>
        /// <param name="runAsSeparateThread">Запускать в отдельном потоке?</param>
        /// <returns>Булевский флаг операции</returns>
        public bool StartToAnalyzeCRC64(String path, String filename, int dataCount, int eccCount, bool runAsSeparateThread)
        {
            // Если поток вычисления CRC-64 работает - не позволяем повторный запуск        
            if (InProcessing)
            {
                return false;
            }

            // Изначально все тома для восстановления считаем поврежденными
            _allEccVolsOK = false;

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
                // Вычисляем и проверяем CRC-64 для каждого из файлов набора с заполнением
                // свойства VolList
                AnalyzeCRC64();

                // Возвращаем результат обработки
                return _processedOK;
            }
            
            // Создаем поток вычисления и проверки CRC-64...
            _thrFileAnalyze = new Thread(new ThreadStart(AnalyzeCRC64));

            //...затем даем ему имя...
            _thrFileAnalyze.Name = "FileAnalyzer.AnalyzeCRC64()";
                
            //...устанавливаем выбранный приоритет задачи...
            _thrFileAnalyze.Priority = _threadPriority;
                        
            //...и запускаем его
            _thrFileAnalyze.Start();

            // Сообщаем, что все нормально
            return true;
        }

        /// <summary>
        /// Метод остановки потока
        /// </summary>
        public void Stop()
        {
            // Указываем, что поток обработки больше не должен выполняться            
            _executeEvent[0].Set(); // Сначала снимаем режим "Пауза"...
            _wakeUpEvent[0].Set();  //...а, затем, снимаем с "дремоты"...     
            _exitEvent[0].Set();    //...и указываем, что требуется выход
        }

        /// <summary>
        /// Постановка потока обработки на паузу
        /// </summary>
        public void Pause()
        {
            // Ставим обработку на паузу
            _executeEvent[0].Reset(); // Сначала устанавливаем режим "Пауза"...
            _wakeUpEvent[0].Set();    //...а, затем, снимаем с "дремоты"...            
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
        /// Вычисление и запись в конец файлов значения CRC-64
        /// </summary>                
        private void WriteCRC64()
        {   
            // Вычисляем значение модуля, который позволит выводить процент обработки
            // ровно при единичном приращении для цикла по "i"
            int progressMod1 = ((_dataCount + _eccCount) / 100);

            // Если модуль равен нулю, то увеличиваем его до значения "1", чтобы
            // прогресс выводился на каждой итерации (файл очень маленький)
            if (progressMod1 == 0)
            {
                progressMod1 = 1;
            }
            
            // Подвергаем обработке все тома
            for (int volNum = 0; volNum < (_dataCount + _eccCount); volNum++)
            {     
                // Считываем первоначальное имя файла
                String filename = _filename;

                // Получаем имя исходного файла в префиксной форме
                _eFileNamer.Pack(ref filename, volNum, _dataCount, _eccCount);

                // Формируем полное имя файла
                filename = _path + filename;
                
                // Производим вычисление CRC-64 для каждого файла
                if (_eFileIntegrityCheck.StartToWriteCRC64(filename, true))
                {
                    // Цикл ожидания завершения обработки файла
                    while (true)
                    {                                            
                        // Если не обнаружили установленного события "_executeEvent",
                        // то пользователь хочет, чтобы мы поставили обработку на паузу -                       
                        if (!ManualResetEvent.WaitAll(_executeEvent, 0, false))
                        {
                            //...приостанавливаем работу контролируемого алгоритма...                      
                            _eFileIntegrityCheck.Pause();

                            //...и сами засыпаем
                            ManualResetEvent.WaitAll(_executeEvent);
                            
                            // А когда проснулись, указываем, что обработка должна продолжаться                            
                            _eFileIntegrityCheck.Continue();
                        }

                        // Ждем любое из перечисленных событий...
                        int eventIdx = ManualResetEvent.WaitAny(new ManualResetEvent[] { _wakeUpEvent[0], _exitEvent[0], _eFileIntegrityCheck.FinishedEvent[0] });

                        //...если получили сигнал к тому, чтобы проснуться -
                        // переходим на новую итерацию, т.к. просыпаемся
                        // перед постановкой на паузу...
                        if (eventIdx == 0)
                        {
                            //...предварительно сбросив событие, заставившее нас проснуться
                            _wakeUpEvent[0].Reset();

                            continue;
                        }

                        //...если получили сигнал к выходу из обработки...
                        if (eventIdx == 1)
                        {
                            //...останавливаем контролируемый алгоритм
                            _eFileIntegrityCheck.Stop();

                            // Указываем на то, что обработка была прервана
                            _processedOK = false;

                            // Активируем индикатор актуального состояния переменных-членов
                            _finished = true;

                            // Устанавливаем событие завершения обработки
                            _finishedEvent[0].Set();

                            return;
                        }

                        //...если получили сигнал о завершении обработки вложенным алгоритмом...
                        if (eventIdx == 2)
                        {
                            //...выходим из цикла ожидания завершения (этого и ждали в while(true)!)
                            break;
                        }

                    } // while(true)

                } else
                {
                    // Сбрасываем флаг корректности результата
                    _processedOK = false;

                    // Активируем индикатор актуального состояния переменных-членов
                    _finished = true;

                    // Устанавливаем событие завершения обработки
                    _finishedEvent[0].Set();

                    return;
                }

                // В связи с закрытием большого количества файловых потоков
                // необходимо дождаться записи изменений, внесенных потоком
                // кодирования в тело класса. Поток уже не работает, но
                // установленное им булевское свойство, возможно, ещё
                // "не проявилось". Достаточно необычная ситуация,
                // но факт - вещь упрямая.
                for (int i = 0; i < 9000; i++)
                {
                    if (!_eFileIntegrityCheck.Finished)
                    {
                        Thread.Sleep(100);
                    }
                }

                // Если 15 минут ожидания закрытия файловых потоков не привели к желаемому
                // результату - это ошибка
                if (!_eFileIntegrityCheck.ProcessedOK)
                {
                    // Указываем на то, что обработка не была завершена корректно
                    _processedOK = false;

                    // Активируем индикатор актуального состояния переменных-членов
                    _finished = true;

                    // Устанавливаем событие завершения обработки
                    _finishedEvent[0].Set();

                    return;
                }

                // Выводим прогресс обработки
                if (
                        ((volNum % progressMod1) == 0)
                     &&
                        (OnUpdateFileAnalyzeProgress != null)
                    )
                {
                    OnUpdateFileAnalyzeProgress(((double)(volNum + 1) / (double)(_dataCount + _eccCount)) * 100.0);
                }

                // В случае, если требуется постановка на паузу, событие "_executeEvent"
                // будет сброшено, и будем на паузе вплоть до его появления
                ManualResetEvent.WaitAll(_executeEvent);

                // Если указано, что требуется выйти из потока - выходим
                if (ManualResetEvent.WaitAll(_exitEvent, 0, false))
                {
                    // Указываем на то, что обработка была прервана
                    _processedOK = false;

                    // Активируем индикатор актуального состояния переменных-членов
                    _finished = true;

                    // Устанавливаем событие завершения обработки
                    _finishedEvent[0].Set();

                    return;
                }      
            }
                       
            // Сообщаем об окончании процесса обработки
            if (OnFileAnalyzeFinish != null)
            {
                OnFileAnalyzeFinish();
            }

            // Сообщаем, что обработка прошла корректно
            _processedOK = true;

            // Активируем индикатор актуального состояния переменных-членов
            _finished = true;

            // Устанавливаем событие завершения обработки
            _finishedEvent[0].Set();
        }
         
	    /// <summary>
        /// Вычисление и проверка значения CRC-64, записанного в конце файла
        /// </summary>        
        private void AnalyzeCRC64()
        {
            // Вычисляем распределение процентов итераций по стадиям для
            // корректной обработки процентов
            int percOfFirstStage  = (int)((100 * _dataCount) / (_dataCount + _eccCount));
            int percOfSecondStage = (int)((100 * _eccCount)  / (_dataCount + _eccCount));

            // Данная стадия должна занимать хотя бы один процент
            // (для корректности расчетов)
            if (percOfSecondStage == 0)
            {
                percOfSecondStage = 1;
            }

            // Вычисляем значение модуля, который позволит выводить процент обработки
            // ровно при единичном приращении для цикла по "i"
            int progressMod1 = (_dataCount / percOfFirstStage);

            // Если модуль равен нулю, то увеличиваем его до значения "1", чтобы
            // прогресс выводился на каждой итерации (файл очень маленький)
            if (progressMod1 == 0)
            {
                progressMod1 = 1;
            }

            // Выделяем память под "_volList"
            _volList = new int[_dataCount];

            // Выделяем память под "_altEccList"
            int[] altEccList = new int[_eccCount];

            // Индекс в массиве томов
            int volListIdx = 0;

            // Индекс в массиве томов для восстановления
            int altEccListIdx = 0;

            // Счетчик количества поврежденных основных томов
            int dataVolMissCount = 0;

            // Счетчик количества найденных томов для восстановления
            int eccVolPresentCount = 0;

            // Имя файла для обработки
            String filename;

            // Подвергаем проверке все основные тома
            for (int dataNum = 0; dataNum < _dataCount; dataNum++)
            {
                // Изначально предполагаем, что текущий том поврежден
                bool dataVolIsOK = false;

                // Считываем первоначальное имя файла
                filename = _filename;
                
                // Получаем имя исходного файла в префиксной форме
                _eFileNamer.Pack(ref filename, dataNum, _dataCount, _eccCount);

                // Формируем полное имя файла
                filename = _path + filename;

                // Если исходный файл существует...
                if (File.Exists(filename))
                {
                    //...- производим его проверку
                    if (_eFileIntegrityCheck.StartToCheckCRC64(filename, true))
                    {
                        // Цикл ожидания завершения обработки файла
                        while (true)
                        {
                            // Если не обнаружили установленного события "_executeEvent",
                            // то пользователь хочет, чтобы мы поставили обработку на паузу -                       
                            if (!ManualResetEvent.WaitAll(_executeEvent, 0, false))
                            {
                                //...приостанавливаем работу контролируемого алгоритма...                      
                                _eFileIntegrityCheck.Pause();

                                //...и сами засыпаем
                                ManualResetEvent.WaitAll(_executeEvent);
                                
                                // А когда проснулись, указываем, что обработка должна продолжаться                            
                                _eFileIntegrityCheck.Continue();
                            }

                            // Ждем любое из перечисленных событий...
                            int eventIdx = ManualResetEvent.WaitAny(new ManualResetEvent[] { _wakeUpEvent[0], _exitEvent[0], _eFileIntegrityCheck.FinishedEvent[0] });

                            //...если получили сигнал к тому, чтобы проснуться -
                            // переходим на новую итерацию, т.к. просыпаемся
                            // перед постановкой на паузу...
                            if (eventIdx == 0)
                            {
                                //...предварительно сбросив событие, заставившее нас проснуться
                                _wakeUpEvent[0].Reset();

                                continue;
                            }

                            //...если получили сигнал к выходу из обработки...
                            if (eventIdx == 1)
                            {
                                //...останавливаем контролируемый алгоритм
                                _eFileIntegrityCheck.Stop();

                                // Указываем на то, что обработка была прервана
                                _processedOK = false;

                                // Активируем индикатор актуального состояния переменных-членов
                                _finished = true;

                                // Устанавливаем событие завершения обработки
                                _finishedEvent[0].Set();

                                return;
                            }

                            //...если получили сигнал о завершении обработки вложенным алгоритмом...
                            if (eventIdx == 2)
                            {
                                //...выходим из цикла ожидания завершения (этого и ждали в while(true)!)
                                break;
                            }

                        } // while(true)

                    } else
                    {
                        // Сбрасываем флаг корректности результата
                        _processedOK = false;

                        // Активируем индикатор актуального состояния переменных-членов
                        _finished = true;

                        // Устанавливаем событие завершения обработки
                        _finishedEvent[0].Set();

                        return;
                    }

                    // В связи с закрытием большого количества файловых потоков
                    // необходимо дождаться записи изменений, внесенных потоком
                    // кодирования в тело класса. Поток уже не работает, но
                    // установленное им булевское свойство, возможно, ещё
                    // "не проявилось". Достаточно необычная ситуация,
                    // но факт - вещь упрямая.
                    for (int i = 0; i < 9000; i++)
                    {
                        if (!_eFileIntegrityCheck.Finished)
                        {
                            Thread.Sleep(100);
                        }
                    }

                    // Указываем, что основной том корректен
                    if (_eFileIntegrityCheck.ProcessedOK)
                    {
                        dataVolIsOK = true;
                    }

                    // Выводим прогресс обработки
                    if (
                            ((dataNum % progressMod1) == 0)
                         &&
                            (OnUpdateFileAnalyzeProgress != null)
                        )
                    {
                        OnUpdateFileAnalyzeProgress(((double)(dataNum + 1) / (double)(_dataCount + _eccCount)) * percOfFirstStage);
                    }

                    // В случае, если требуется постановка на паузу, событие "_executeEvent"
                    // будет сброшено, и будем на паузе вплоть до его появления
                    ManualResetEvent.WaitAll(_executeEvent);

                    // Если указано, что требуется выйти из потока - выходим
                    if (ManualResetEvent.WaitAll(_exitEvent, 0, false))
                    {
                        // Указываем на то, что обработка была прервана
                        _processedOK = false;

                        // Активируем индикатор актуального состояния переменных-членов
                        _finished = true;

                        // Устанавливаем событие завершения обработки
                        _finishedEvent[0].Set();

                        return;
                    }      
                }

                // Если данный основной том не поврежден, записываем его в "_volList",
                // а иначе увеличиваем счетчик поврежденных томов и ставим на место
                // номера тома значение "-1", которое укажет на необходимость подстановки
                // тома для восстановления
                if (dataVolIsOK)
                {
                    _volList[volListIdx++] = dataNum;

                } else
                {
                    _volList[volListIdx++] = -1;

                    // Увеличиваем счетчик количества поврежденных основных томов
                    dataVolMissCount++;
                }
            }

            // Если повреждены все основные тома - восстановление в принципе невозможно
            if (dataVolMissCount == _dataCount)
            {
                // Сообщаем, что восстановление архива невозможно - повреждены все
                // основные тома
                if (OnGetDamageStat != null)
                {
                    OnGetDamageStat(-1, -1);
                }

                // Указываем на то, что данные не могут быть восстановлены
                _processedOK = false;

                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();
                
                return;
            }
                               
            // Теперь, когда знаем количество поврежденных основных томов,
            // нужно просканировать все файлы для восстановления, и определить
            // требуемую их часть в список томов, а "избыток" поместить в
            // список альтренативных томов для восстановления

            // Вычисляем значение модуля, который позволит выводить процент обработки
            // ровно при единичном приращении для цикла по "i"
            progressMod1 = (_eccCount / percOfSecondStage);

            // Если модуль равен нулю, то увеличиваем его до значения "1", чтобы
            // прогресс выводился на каждой итерации (файл очень маленький)
            if (progressMod1 == 0)
            {
                progressMod1 = 1;
            }

            // Анализируем состав томов для восстановления
            for (int eccNum = _dataCount; eccNum < (_dataCount + _eccCount); eccNum++)
            {
                // Изначально предполагаем, что текущий том поврежден
                bool eccVolIsOK = false;

                // Считываем первоначальное имя файла
                filename = _filename;

                // Получаем имя исходного файла в префиксной форме
                _eFileNamer.Pack(ref filename, eccNum, _dataCount, _eccCount);

                // Формируем полное имя файла
                filename = _path + filename;

                // Если исходный файл существует...
                if (File.Exists(filename))
                {
                    //...- производим его проверку
                    if (_eFileIntegrityCheck.StartToCheckCRC64(filename, true))
                    {
                        // Цикл ожидания завершения обработки файла
                        while (true)
                        {
                            // Если не обнаружили установленного события "_executeEvent",
                            // то пользователь хочет, чтобы мы поставили обработку на паузу -                       
                            if (!ManualResetEvent.WaitAll(_executeEvent, 0, false))
                            {
                                //...приостанавливаем работу контролируемого алгоритма...                      
                                _eFileIntegrityCheck.Pause();

                                //...и сами засыпаем
                                ManualResetEvent.WaitAll(_executeEvent);
                                
                                // А когда проснулись, указываем, что обработка должна продолжаться                            
                                _eFileIntegrityCheck.Continue();
                            }

                            // Ждем любое из перечисленных событий...
                            int eventIdx = ManualResetEvent.WaitAny(new ManualResetEvent[] { _wakeUpEvent[0], _exitEvent[0], _eFileIntegrityCheck.FinishedEvent[0] });

                            //...если получили сигнал к тому, чтобы проснуться -
                            // переходим на новую итерацию, т.к. просыпаемся
                            // перед постановкой на паузу...
                            if (eventIdx == 0)
                            {
                                //...предварительно сбросив событие, заставившее нас проснуться
                                _wakeUpEvent[0].Reset();

                                continue;
                            }

                            //...если получили сигнал к выходу из обработки...
                            if (eventIdx == 1)
                            {
                                //...останавливаем контролируемый алгоритм
                                _eFileIntegrityCheck.Stop();

                                // Указываем на то, что обработка была прервана
                                _processedOK = false;

                                // Активируем индикатор актуального состояния переменных-членов
                                _finished = true;

                                // Устанавливаем событие завершения обработки
                                _finishedEvent[0].Set();

                                return;
                            }

                            //...если получили сигнал о завершении обработки вложенным алгоритмом...
                            if (eventIdx == 2)
                            {
                                //...выходим из цикла ожидания завершения (этого и ждали в while(true)!)
                                break;
                            }

                        } // while(true)

                    } else
                    {
                        // Сбрасываем флаг корректности результата
                        _processedOK = false;

                        // Активируем индикатор актуального состояния переменных-членов
                        _finished = true;

                        // Устанавливаем событие завершения обработки
                        _finishedEvent[0].Set();

                        return;
                    }

                    // В связи с закрытием большого количества файловых потоков
                    // необходимо дождаться записи изменений, внесенных потоком
                    // кодирования в тело класса. Поток уже не работает, но
                    // установленное им булевское свойство, возможно, ещё
                    // "не проявилось". Достаточно необычная ситуация,
                    // но факт - вещь упрямая.
                    for (int i = 0; i < 9000; i++)
                    {
                        if (!_eFileIntegrityCheck.Finished)
                        {
                            Thread.Sleep(100);
                        }
                    }

                    // Указываем, что том для восстановления корректен
                    if (_eFileIntegrityCheck.ProcessedOK)
                    {
                        eccVolIsOK = true;
                    }

                    // Выводим прогресс обработки
                    if (
                            ((eccNum % progressMod1) == 0)
                         &&
                            (OnUpdateFileAnalyzeProgress != null)
                        )
                    {
                        OnUpdateFileAnalyzeProgress((((double)(eccNum + 1) / (double)(_dataCount + _eccCount)) * percOfSecondStage) + percOfFirstStage);
                    }

                    // В случае, если требуется постановка на паузу, событие "_executeEvent"
                    // будет сброшено, и будем на паузе вплоть до его появления
                    ManualResetEvent.WaitAll(_executeEvent);

                    // Если указано, что требуется выйти из потока - выходим
                    if (ManualResetEvent.WaitAll(_exitEvent, 0, false))
                    {
                        // Указываем на то, что обработка была прервана
                        _processedOK = false;

                        // Активируем индикатор актуального состояния переменных-членов
                        _finished = true;

                        // Устанавливаем событие завершения обработки
                        _finishedEvent[0].Set();

                        return;
                    }      
                }

                // Если том для восстановления хороший...
                if (eccVolIsOK)
                {
                    //...- добавляем его в список
                    altEccList[altEccListIdx++] = eccNum;

                    // Увеличиваем счетчик количества томов для восстановления
                    eccVolPresentCount++;

                } else
                {
                    // Если встретился хотя бы один поврежденный том для восстановления,
                    // и мы находимся в состоянии неповрежденности всех основных томов,
                    // то последующее сканирование томов для восстановления нецелесообразно
                    if (dataVolMissCount == 0)
                    {
                        // Сообщаем об окончании процесса обработки
                        if (OnFileAnalyzeFinish != null)
                        {
                            OnFileAnalyzeFinish();
                        }

                        // Выводим статистику повреждений (-1 означает, что величина не определена)
                        if (OnGetDamageStat != null)
                        {
                            OnGetDamageStat(0, -1);
                        }

                        // Указываем на то, что данные не повреждены
                        _processedOK = true;

                        // Активируем индикатор актуального состояния переменных-членов
                        _finished = true;

                        // Устанавливаем событие завершения обработки
                        _finishedEvent[0].Set();

                        return;
                    }

                    //...а иначе указываем, что том поврежден
                    altEccList[altEccListIdx++] = -1;
                }
            }

            // Если значение счетчика количества корректных томов для восстановления совпадает
            // со значением счетчика томов для восстановления конфигурации - все тома для
            // восстановления являются неповрежденными
            if (eccVolPresentCount == _eccCount)
            {
                _allEccVolsOK = true;
            }

            // Если нет поврежденных основных томов, просто выходим
            // (предварительно просканировав все тома для восстановления, т.к.
            // нужно узнать, есть ли поврежденные среди них)
            if (dataVolMissCount == 0)
            {
                // Сообщаем об окончании процесса обработки
                if (OnFileAnalyzeFinish != null)
                {
                    OnFileAnalyzeFinish();
                }

                // Выводим статистику повреждений (-1 означает, что величина не определена)
                if (OnGetDamageStat != null)
                {
                    OnGetDamageStat(0, -1);
                }

                // Указываем на то, что данные не повреждены
                _processedOK = true;

                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return;
            }
           
            // Если мы не сможем восстановить повреждения...
            if (eccVolPresentCount < dataVolMissCount)               
            {
                //...выводим статистику анализа томов...
                if (OnGetDamageStat != null)
                {
                    // Вычисляем общий процент повреждений (сумму повреждений
                    // основных томов и томов для восстановления делим на общее количество томов)
                    double percOfDamage = ((double)(dataVolMissCount + (_eccCount - eccVolPresentCount)) / (double)(_dataCount + _eccCount)) * 100;

                    // Вычисляем процент "выживших" альтернативных томов для восстановления (относительно
                    // всего набора томов). Альтернативные тома - это изначально те тома, которые не
                    // планируется использовать для восстановления
                    double percOfAltEcc = ((double)(eccVolPresentCount - dataVolMissCount) / (double)(_dataCount + _eccCount)) * 100;

                    OnGetDamageStat(percOfDamage, percOfAltEcc);
                }

                //...и указываем на то, что данные не могут быть восстановлены
                _processedOK = false;

                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return;
            }
         
            // Перемещаемся на начало списка альтернативных томов для восстановления
            altEccListIdx = 0;
            
            // Теперь пробегаемся по вектору "_volList", и вместо каждого из значений "-1"
            // подставляем очередное значение из найденного диапазона
            for (int i = 0; i < _dataCount; i++)
            {
                if (_volList[i] == -1)
                {                   
                    // Пробегаемся по вектору томов для восстановления,
                    // останавливаясь на корректном томе для восстановления
                    while (altEccList[altEccListIdx] == -1)
                    {
                        altEccListIdx++;                        
                    }

                    // Подставляем на место поврежденного основного тома
                    // том для восстановления,...
                    _volList[i] = altEccList[altEccListIdx];

                    //...убирая использованный том из списка альтернативных
                    altEccList[altEccListIdx] = -1;           
                }
            }
                
            // Сообщаем об окончании процесса обработки
            if (OnFileAnalyzeFinish != null)
            {
                OnFileAnalyzeFinish();
            }

            // Выводим статистику повреждений
            if (OnGetDamageStat != null)
            {
                // Вычисляем общий процент повреждений (сумму повреждений
                // основных томов и томов для восстановления делим на общее количество томов)
                double percOfDamage = ((double)(dataVolMissCount + (_eccCount - eccVolPresentCount)) / (double)(_dataCount + _eccCount)) * 100;

                // Вычисляем процент "выживших" альтернативных томов для восстановления (относительно
                // всего набора томов). Альтернативные тома - это изначально те тома, которые не
                // планируется использовать для восстановления
                double percOfAltEcc = ((double)(eccVolPresentCount - dataVolMissCount) / (double)(_dataCount + _eccCount)) * 100;
                
                OnGetDamageStat(percOfDamage, percOfAltEcc);
            }
           
            // Сообщаем, что обработка прошла корректно
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
        /// Булевское свойство "Множество файлов обработано корректно?"
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
        /// Список порядковых номеров имеющихся томов
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
        /// Все тома для восстановления корректны?
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

                    // Устанавливаем выбранный приоритет процесса                
                    _thrFileAnalyze.Priority = _threadPriority;
                    
                    if (_eFileIntegrityCheck != null)
                    {
                        _eFileIntegrityCheck.ThreadPriority = value;
                    }
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

        // Модуль для упаковки (распаковки) имени файла в префиксный формат
        private FileNamer _eFileNamer;

        // Экземпляр класса контроля целостности набора файлов
        private FileIntegrityCheck _eFileIntegrityCheck;
               
        // Путь к файлам для обработки
        private String _path;

        // Имя файла, которому принадлежит множество томов
        private String _filename;
        
        // Количество основных томов
        private int _dataCount;

        // Количество томов для восстановления
        private int _eccCount;

        // Вектор, указывающий на состав томов
        private int[] _volList;

        // Все тома для восстановления корректны?
        private bool _allEccVolsOK;

        // Экземляр класса полностью закончил обработку?
        private bool _finished;

        // Обработка набора файлов произведена корректно?
        private bool _processedOK;
  
        // Поток контроля целостности файла
        private Thread _thrFileAnalyze;

        // Приоритет процесса контроля целостности файлов
        private ThreadPriority _threadPriority;

        // Событие прекращения обработки файлов
        private ManualResetEvent[] _exitEvent;

        // Событие продолжения обработки файлов
        private ManualResetEvent[] _executeEvent;

        // Событие "пробуждения" цикла ожидания
        private ManualResetEvent[] _wakeUpEvent;

        // Событие, устанавливаемое по завершении обработки
        private ManualResetEvent[] _finishedEvent;
     
        #endregion Data

    } // class FileAnalyzer
}
