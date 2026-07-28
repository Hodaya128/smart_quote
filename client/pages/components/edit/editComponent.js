const apiRoot = BASE_URL + '/api/components';
let sku;

$(document).ready(function () {
    loadComponent();

    $('#editComponentForm').submit(function (e) {
        e.preventDefault();
        setLoading(true);

        const body = {
            componentSKU: sku,
            description: $('#description').val(),
            baseUnit: $('#baseUnit').val(),
            alternativeSKU: $('#alternativeSKU').val() || null
        };

        ajaxCall("PUT", apiRoot + '/' + sku, JSON.stringify(body), editComponentSCB, editComponentECB);
    });
});

function loadComponent() {
    sku = new URLSearchParams(window.location.search).get('sku');
    if (!sku) { alert('מק"ט חסר'); return; }
    ajaxCall("GET", apiRoot + '/' + sku, "", loadComponentSCB, loadComponentECB);
}

function loadComponentSCB(data) {
    $('#description').val(data.description);
    $('#baseUnit').val(data.baseUnit);
    $('#alternativeSKU').val(data.alternativeSKU);
}

function loadComponentECB(xhr, status, error) {
    console.log('Error loading component:', status, error);
    alert('שגיאה בטעינת הרכיב');
}

function setLoading(on) {
    $('#saveBtn').toggle(!on);
    $('#loader').toggleClass('active', on);
}

function editComponentSCB() {
    window.location.href = '/pages/components';
}

function editComponentECB(xhr, status, err) {
    console.log('Error updating:', status, err);
    alert('שגיאה בעדכון, נסה שוב');
    setLoading(false);
}
