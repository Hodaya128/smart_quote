// ====================================================================
// Home Dashboard
// ============== נתונים + 3 גרפים קבועים + גרף AI דינאמי ==============
// ====================================================================

const quotesApi = BASE_URL + "/api/quotes";
const insightsApi = BASE_URL + "/api/insights/chart";

// ה-instances של הגרפים — נשמור אותם כדי לעשות destroy לפני רינדור מחדש
let revenueChart = null;
let statusChart = null;
let customerChart = null;
let aiChart = null;

// מיפוי סטטוסים לעברית (לגרפים — ללא HTML)
const statusHebrew = {
    'Draft':      'טיוטה',
    'Processing': 'מעבד...',
    'Completed':  'הושלם',
    'Error':      'שגיאה',
    'Sent':       'נשלח',
    'Approved':   'אושר',
    'Rejected':   'נדחה'
};

// שמות חודשים בעברית (index 1 = ינואר)
const hebMonths = ['', 'ינואר', 'פברואר', 'מרץ', 'אפריל', 'מאי', 'יוני',
                   'יולי', 'אוגוסט', 'ספטמבר', 'אוקטובר', 'נובמבר', 'דצמבר'];

// צבעים עקביים לגרפים
const chartColors = {
    revenue: '#5b7fa6',
    profit: '#27ae60',
    statusPalette: ['#3498db', '#e67e22', '#27ae60', '#c0392b', '#9b59b6', '#f39c12', '#95a5a6'],
    customer: '#e67e22'
};

$(document).ready(function () {
    // טעינת ההצעות וציור הגרפים הקבועים
    ajaxCall("GET", quotesApi, "", onQuotesLoaded, onQuotesError);

    // כפתור יצירת גרף עם AI
    $('#aiGenerateBtn').click(onGenerateAiChart);

    // מאפשר גם Enter במקום לחיצה
    $('#aiPrompt').keypress(function (e) {
        if (e.which === 13) onGenerateAiChart();
    });
});

// ============== טעינת הנתונים ==============
function onQuotesLoaded(data) {
    renderFixedCharts(data || []);
}

function onQuotesError(xhr, status, error) {
    console.log('Error loading quotes:', status, error);
    // עדיין ננסה לצייר עם מערך ריק כדי שהמסך לא ייראה שבור
    renderFixedCharts([]);
}

// ============== 3 גרפים קבועים ==============
function renderFixedCharts(quotes) {
    renderRevenueByMonth(quotes);
    renderStatusPie(quotes);
    renderProfitByCustomer(quotes);
}

// --- גרף הכנסות ורווח לפי חודש ---
function renderRevenueByMonth(quotes) {
    // קיבוץ לפי "שנה-חודש"
    const grouped = {};
    quotes.forEach(q => {
        if (!q.createdDate) return;
        const d = new Date(q.createdDate);
        const key = d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0');
        if (!grouped[key]) grouped[key] = { revenue: 0, profit: 0, month: d.getMonth() + 1, year: d.getFullYear() };
        grouped[key].revenue += Number(q.finalTotalPrice) || 0;
        grouped[key].profit += Number(q.totalProfit) || 0;
    });

    // מיון לפי חודש-שנה וחיתוך 12 האחרונים
    const sortedKeys = Object.keys(grouped).sort().slice(-12);
    const labels = sortedKeys.map(k => {
        const info = grouped[k];
        return hebMonths[info.month] + ' ' + info.year;
    });
    const revenueData = sortedKeys.map(k => grouped[k].revenue.toFixed(2));
    const profitData = sortedKeys.map(k => grouped[k].profit.toFixed(2));

    if (revenueChart) revenueChart.destroy();
    const ctx = document.getElementById('revenueByMonthCanvas').getContext('2d');
    revenueChart = new Chart(ctx, {
        type: 'line',
        data: {
            labels: labels,
            datasets: [
                {
                    label: 'הכנסות ($)',
                    data: revenueData,
                    borderColor: chartColors.revenue,
                    backgroundColor: chartColors.revenue + '33',
                    tension: 0.3,
                    fill: true
                },
                {
                    label: 'רווח ($)',
                    data: profitData,
                    borderColor: chartColors.profit,
                    backgroundColor: chartColors.profit + '33',
                    tension: 0.3,
                    fill: true
                }
            ]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: { position: 'bottom' }
            },
            scales: {
                y: { beginAtZero: true }
            }
        }
    });
}

// --- גרף התפלגות סטטוסים ---
function renderStatusPie(quotes) {
    const counts = {};
    quotes.forEach(q => {
        const s = q.status || 'Unknown';
        counts[s] = (counts[s] || 0) + 1;
    });

    const labels = Object.keys(counts).map(s => statusHebrew[s] || s);
    const values = Object.values(counts);
    const colors = values.map((_, i) => chartColors.statusPalette[i % chartColors.statusPalette.length]);

    if (statusChart) statusChart.destroy();
    const ctx = document.getElementById('statusPieCanvas').getContext('2d');
    statusChart = new Chart(ctx, {
        type: 'doughnut',
        data: {
            labels: labels,
            datasets: [{
                data: values,
                backgroundColor: colors,
                borderWidth: 2,
                borderColor: '#fff'
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: { position: 'bottom' }
            }
        }
    });
}

// --- גרף Top 10 לקוחות לפי רווח ---
function renderProfitByCustomer(quotes) {
    const totals = {};
    quotes.forEach(q => {
        const name = (q.customer && q.customer.customerName) ? q.customer.customerName : 'לא ידוע';
        totals[name] = (totals[name] || 0) + (Number(q.totalProfit) || 0);
    });

    // מיון יורד וחיתוך Top 10
    const sorted = Object.entries(totals)
        .sort((a, b) => b[1] - a[1])
        .slice(0, 10);

    const labels = sorted.map(pair => pair[0]);
    const values = sorted.map(pair => pair[1].toFixed(2));

    if (customerChart) customerChart.destroy();
    const ctx = document.getElementById('profitByCustomerCanvas').getContext('2d');
    customerChart = new Chart(ctx, {
        type: 'bar',
        data: {
            labels: labels,
            datasets: [{
                label: 'רווח ($)',
                data: values,
                backgroundColor: chartColors.customer + 'cc',
                borderColor: chartColors.customer,
                borderWidth: 1
            }]
        },
        options: {
            indexAxis: 'y', // ציר אופקי — קל יותר לקריאת שמות לקוחות
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: { display: false }
            },
            scales: {
                x: { beginAtZero: true }
            }
        }
    });
}

// ============== AI Chart ==============
function onGenerateAiChart() {
    const prompt = $('#aiPrompt').val().trim();
    $('#aiChartError').text('');

    if (!prompt) {
        $('#aiChartError').text('יש להזין שאלה');
        return;
    }

    $('#aiLoading').show();

    ajaxCall(
        "POST",
        insightsApi,
        JSON.stringify({ prompt: prompt }),
        onAiSuccess,
        onAiError
    );
}

function onAiSuccess(response) {
    $('#aiLoading').hide();

    if (!response || !response.chartConfig) {
        $('#aiChartError').text('לא התקבל גרף מהשרת');
        return;
    }

    try {
        if (aiChart) aiChart.destroy();
        const ctx = document.getElementById('aiDynamicCanvas').getContext('2d');

        // להבטיח responsive ו-maintainAspectRatio=false כדי שייכנס בקופסה
        const config = response.chartConfig;
        config.options = config.options || {};
        config.options.responsive = true;
        config.options.maintainAspectRatio = false;

        aiChart = new Chart(ctx, config);
    } catch (ex) {
        console.log('Error rendering AI chart:', ex);
        $('#aiChartError').text('שגיאה בציור הגרף');
    }
}

function onAiError(xhr, status, error) {
    $('#aiLoading').hide();
    console.log('AI error:', status, error, xhr.responseText);

    let msg = 'שגיאה ביצירת הגרף';
    try {
        const body = xhr.responseJSON;
        if (body && body.error) msg += ': ' + body.error;
    } catch (e) { }

    $('#aiChartError').text(msg);
}
