const apiRoot = BASE_URL + '/api/suppliers';
let id;

$(document).ready(function () {
    loadSupplier();

    $('#editSupplierForm').submit(function (e) {
        e.preventDefault();
        save();
    });
});

function setLoading(on) {
    $('#saveBtn').toggle(!on);
    $('#loader').toggleClass('active', on);
}

function moveToSuppliers() {
    window.location.href = '/pages/suppliers';
}

function loadSupplier() {
    id = new URLSearchParams(window.location.search).get('id');
    if (!id) { alert('מזהה ספק חסר'); return; }
    ajaxCall("GET", apiRoot + '/' + id, "", loadSupplierSCB, loadSupplierECB);
}

function loadSupplierSCB(data) {
    $('#supplierName').val(data.supplierName);
    $('#websiteUrl').val(data.websiteUrl);
}

function loadSupplierECB(xhr, status, error) {
    console.log('Error loading supplier:', status, error);
    alert('שגיאה בטעינת הספק');
}

function save() {
    setLoading(true);

    const body = {
        supplierName: $('#supplierName').val(),
        websiteUrl: $('#websiteUrl').val()
    };

    ajaxCall("PUT", apiRoot + '/' + id, JSON.stringify(body), moveToSuppliers, saveECB);
}

function saveECB(xhr, status, error) {
    console.log('Error updating:', status, error);
    alert('שגיאה בעדכון, נסה שוב');
    setLoading(false);
}
