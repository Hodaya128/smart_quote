const apiRoot = BASE_URL + '/api/suppliers';

$(document).ready(function () {
    $('#addSupplierForm').submit(function (e) {
        e.preventDefault();
        addNew();
    });
});

function setLoading(on) {
    $('#saveBtn').toggle(!on);
    $('#loader').toggleClass('active', on);
}

function moveToSuppliers() {
    window.location.href = '/pages/suppliers';
}

function addNew() {
    setLoading(true);

    const body = {
        supplierName: $('#supplierName').val(),
        websiteUrl: $('#websiteUrl').val()
    };

    ajaxCall("POST", apiRoot, JSON.stringify(body), moveToSuppliers, addNewECB);
}

function addNewECB(xhr, status, err) {
    console.log('Error:', status, err);
    alert('שגיאה בשמירה, נסה שוב');
    setLoading(false);
}
